// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新建 CLI 会话驱动层。原 Windows 版由 WPF 的 DispatcherSynchronizationContext 承接
//       LinkSession 的 Post()，这里换成单线程消息泵；Changed 事件在后台线程同步触发，
//       因此只在事件里置标志，所有读状态 / 打印 / 写快照都放在主线程循环里做。
//       host/join 是前台长驻进程，状态经 session.json 落盘，status/peers/logs/stop 从文件读取。

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Campfire.Link;
using Campfire.Link.Runtime;

namespace Campfire.Cli
{
    /// <summary>替代 WPF DispatcherSynchronizationContext：Post 入队，由主循环 PumpAll 出队执行。</summary>
    internal sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _queue =
            new ConcurrentQueue<(SendOrPostCallback, object)>();

        public override void Post(SendOrPostCallback d, object state)
        {
            if (d != null)
                _queue.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            d?.Invoke(state);
        }

        /// <summary>执行队列里所有回调；单个回调失败不影响其余回调。</summary>
        public int PumpAll()
        {
            int count = 0;
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[pump] 回调异常：" + ex.Message);
                }
                count++;
            }
            return count;
        }
    }

    /// <summary>host/join 进程写给 status/peers 查询命令的状态快照。</summary>
    internal sealed class SessionSnapshot
    {
        public SessionSnapshot()
        {
        }

        public string Role { get; set; }
        public string Stage { get; set; }
        public string Code { get; set; }
        public int HostPort { get; set; }
        public int LocalPort { get; set; }
        public int ConnectedNodes { get; set; }
        public int DirectPeers { get; set; }
        public int RelayedPeers { get; set; }
        public int PlayerCount { get; set; }
        public string[] PlayerNames { get; set; }
        public string Error { get; set; }
        public string DataDirectory { get; set; }
        public int ProcessId { get; set; }
        public long UpdatedUnix { get; set; }
    }

    internal static class SessionState
    {
        public static string SnapshotPath => Path.Combine(AppSettings.DataDirectory, "session.json");

        public static string StopFlagPath => Path.Combine(AppSettings.DataDirectory, "stop.requested");

        public static bool StopRequested => File.Exists(StopFlagPath);

        public static void Write(SessionSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                string temp = SnapshotPath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, SnapshotPath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 快照只是给查询命令看的，写失败不影响联机主流程。
            }
        }

        public static SessionSnapshot Read()
        {
            try
            {
                if (!File.Exists(SnapshotPath))
                    return null;
                return JsonSerializer.Deserialize<SessionSnapshot>(File.ReadAllText(SnapshotPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }

        public static void RequestStop()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                File.WriteAllText(StopFlagPath, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("无法写入停止标记：" + ex.Message, ex);
            }
        }

        /// <summary>清掉上一次运行残留的快照与停止标记；只在 host/join 启动时调用。</summary>
        public static void Clear()
        {
            TryDelete(SnapshotPath);
            TryDelete(StopFlagPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>一次 host/join 会话的生命周期。所有 LinkSession 状态只在主循环里读取。</summary>
    internal sealed class CliHost
    {
        private readonly PumpSynchronizationContext _ui = new PumpSynchronizationContext();
        private readonly LinkSession _session;
        private volatile bool _changed = true;
        private volatile bool _stop;
        private bool _announced;

        public CliHost()
        {
            _session = new LinkSession(_ui);
            _session.Changed += (sender, e) => _changed = true;
        }

        public int RunHost(SessionOptions options) => Drive(options, true);

        public int RunJoin(SessionOptions options) => Drive(options, false);

        private int Drive(SessionOptions options, bool hosting)
        {
            // 这台开发机是 Windows：EasyTier Linux 二进制与 /proc 适配层都需要真正的 Linux。
            // 明确拒绝比让它卡在下载组件上更诚实。
            if (!OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine("当前不是 Linux：EasyTier 的 Linux 二进制和 /proc 适配层需要 Ubuntu 等 Linux 环境。");
                Console.Error.WriteLine("请把 linux/ 目录整个拷到 Ubuntu 后执行；本机可先用 selftest / doctor 验证代码。");
                return 2;
            }

            ApplyCliDefaults(options);
            SessionState.Clear();
            Directory.CreateDirectory(AppSettings.DataDirectory);
            Directory.CreateDirectory(LinkSettings.DataDirectory);

            ConsoleCancelEventHandler cancelHandler = (sender, e) =>
            {
                e.Cancel = true;      // 阻止进程被直接终止，让下面的清理流程跑完
                _stop = true;
            };
            Console.CancelKeyPress += cancelHandler;

            if (!EasyTierPackage.IsInstalled())
            {
                Console.WriteLine("EasyTier " + EasyTierPackage.Version + " 组件未安装，将尝试自动下载。");
                Console.WriteLine("学校网慢的话，可先执行：campfire easytier install，或用 linux/fetch-easytier.sh 断点续传后手工放置。");
            }

            int exitCode = 0;
            try
            {
                if (hosting)
                {
                    Console.WriteLine("正在创建房间，房主端口 " + options.Port + " …");
                    _session.StartHost(options.Port, null);
                }
                else
                {
                    RoomCode code = RoomCode.Find(options.Code);
                    if (code == null)
                    {
                        Console.Error.WriteLine("邀请码无效：" + options.Code);
                        return 2;
                    }
                    Console.WriteLine("正在加入房间 " + code.Code + " …");
                    _session.StartJoin(code);
                }

                DateTime lastWrite = DateTime.MinValue;
                while (true)
                {
                    _ui.PumpAll();

                    if (_changed)
                    {
                        _changed = false;
                        PrintStatus(hosting);
                    }

                    if (_session.Stage == LinkStage.Ready && !_announced)
                    {
                        _announced = true;
                        PrintReadyBanner(hosting);
                    }

                    if ((DateTime.Now - lastWrite).TotalSeconds >= 2)
                    {
                        WriteSnapshot(hosting);
                        lastWrite = DateTime.Now;
                    }

                    if (_session.Stage == LinkStage.Failed)
                    {
                        Console.Error.WriteLine("联机失败：" + DescribeError());
                        WriteSnapshot(hosting);
                        exitCode = 1;
                        break;
                    }

                    if (_stop || SessionState.StopRequested)
                    {
                        Console.WriteLine("收到停止请求，正在释放房间与 EasyTier 进程…");
                        break;
                    }

                    Thread.Sleep(100);
                }
            }
            finally
            {
                _session.Stop();
                _session.Shutdown();
                _ui.PumpAll();
                Console.CancelKeyPress -= cancelHandler;
                SessionState.Clear();
            }
            return exitCode;
        }

        /// <summary>
        /// 显式关掉已被裁剪、但原版默认开启的功能，避免它们触发空实现或找不到游戏目录的分支。
        /// 这些值会写进 settings.ini；每次 host/join 都会重新设置，行为稳定。
        /// </summary>
        private static void ApplyCliDefaults(SessionOptions options)
        {
            LinkSession.SkipProbe = options.SkipProbe;
            LinkSettings.AutoOpenLan = false;      // 房主自己在 MC 里点「对局域网开放」
            LinkSettings.BackupWorld = false;      // 第一阶段不移植世界备份
            LinkSettings.Voice = false;            // 语音已裁剪
            LinkSettings.ShareMods = false;        // 模组同步已裁剪
            LinkSettings.ServeMods = false;
            LinkSettings.AutoCopyCode = false;     // 无剪贴板
            LinkSettings.DetectClipboard = false;

            if (!string.IsNullOrWhiteSpace(options.Player))
                LinkSettings.PlayerName = options.Player.Trim();
            if (options.LocalPort > 0)
                LinkSettings.LocalPort = options.LocalPort;
            if (options.HideIp)
                LinkSettings.HideAddress = true;
            if (options.NoRelay)
                LinkSettings.AllowRelay = false;
            if (options.ShareLan)
                LinkSettings.ShareWithLan = true;
            if (!string.IsNullOrWhiteSpace(options.Node))
            {
                LinkSettings.CustomNodes = options.Node.Trim();
                LinkSettings.UsePublicNodes = false;
                LinkSettings.AutoPickNodes = false;
            }
            if (options.TimeoutSeconds > 0)
                LinkSettings.SearchTimeoutSeconds = options.TimeoutSeconds;
        }

        private void PrintStatus(bool hosting)
        {
            string steps = string.Join(" ", (_session.Steps ?? Array.Empty<LinkStep>())
                .Select(step => step + ":" + StepText(_session.GetStepState(step))));
            int players = _session.Players?.Count ?? 0;
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + StageText(_session.Stage)
                + " | " + steps
                + " | 节点 " + _session.ConnectedNodes
                + " | 直连 " + _session.DirectPeers + " 中转 " + _session.RelayedPeers
                + " | 玩家 " + players);
        }

        private void PrintReadyBanner(bool hosting)
        {
            Console.WriteLine();
            if (hosting)
            {
                Console.WriteLine("========== 房间已就绪 ==========");
                Console.WriteLine("邀请码   : " + (_session.Code?.Code ?? "(未生成)"));
                Console.WriteLine("房主端口 : " + _session.MinecraftPort);
                Console.WriteLine();
                Console.WriteLine("把邀请码发给朋友，官方 Windows 版 Campfire 输入邀请码即可加入。");
                Console.WriteLine("提醒：请先在 Minecraft 里点「对局域网开放」，端口与 --port 一致。");
            }
            else
            {
                Console.WriteLine("========== 已加入房间 ==========");
                Console.WriteLine("本地端口 : " + _session.LocalPort);
                Console.WriteLine();
                Console.WriteLine("Minecraft 多人游戏里连接 127.0.0.1:" + _session.LocalPort);
            }
            Console.WriteLine("保持本窗口运行；Ctrl+C 或另开终端执行 campfire stop 结束。");
            Console.WriteLine("================================");
            Console.WriteLine();
        }

        private string DescribeError()
        {
            var error = _session.Error;
            if (error == null)
                return "未知错误（查看日志：" + LinkLog.FilePath + "）";
            return error.Message + "（日志：" + LinkLog.FilePath + "）";
        }

        private void WriteSnapshot(bool hosting)
        {
            var players = _session.Players;
            SessionState.Write(new SessionSnapshot
            {
                Role = _session.Role.ToString(),
                Stage = _session.Stage.ToString(),
                Code = _session.Code?.Code,
                HostPort = _session.MinecraftPort,
                LocalPort = _session.LocalPort,
                ConnectedNodes = _session.ConnectedNodes,
                DirectPeers = _session.DirectPeers,
                RelayedPeers = _session.RelayedPeers,
                PlayerCount = players?.Count ?? 0,
                PlayerNames = players?.Select(p => p.Name).ToArray() ?? Array.Empty<string>(),
                Error = _session.Error?.Message,
                DataDirectory = AppSettings.DataDirectory,
                ProcessId = Environment.ProcessId,
                UpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
        }

        private static string StageText(LinkStage stage)
        {
            switch (stage)
            {
                case LinkStage.Idle: return "空闲";
                case LinkStage.Working: return "准备中";
                case LinkStage.Ready: return "就绪";
                case LinkStage.Failed: return "失败";
                default: return stage.ToString();
            }
        }

        private static string StepText(StepState state)
        {
            switch (state)
            {
                case StepState.Active: return "进行中";
                case StepState.Done: return "完成";
                default: return "等待";
            }
        }
    }

    /// <summary>host/join 的命令行选项；由 SessionCommands 解析后传入 CliHost。</summary>
    internal sealed class SessionOptions
    {
        public int Port;
        public string Code;
        public string Player;
        public int LocalPort;
        public bool SkipProbe;
        public bool HideIp;
        public bool NoRelay;
        public bool ShareLan;
        public string Node;
        public int TimeoutSeconds;
    }
}
