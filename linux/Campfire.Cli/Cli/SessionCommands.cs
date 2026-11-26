// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新建 host/join/status/peers/logs/stop 命令层。
//       CLI 是「一个命令一个进程」，所以长驻的 host/join 把状态写到 session.json，
//       查询类命令读该文件；多实例回环测试必须用同一个 --data-dir。

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Campfire.Link;

namespace Campfire.Cli
{
    internal static class SessionCommands
    {
        public static int Host(string[] args)
        {
            if (!TryParse(args, out SessionOptions options, out string error))
                return Reject(error);
            if (options.Port <= 0 || options.Port > 65535)
                return Reject("host 必须带 --port <1..65535>：Linux 版不做自动发现端口，需要你在游戏里手动「对局域网开放」后把端口告诉程序。");
            if (!string.IsNullOrWhiteSpace(options.Code))
                return Reject("host 不接受 --code；要复用旧邀请码请用后续版本。");
            return new CliHost().RunHost(options);
        }

        public static int Join(string[] args)
        {
            if (!TryParse(args, out SessionOptions options, out string error))
                return Reject(error);
            if (string.IsNullOrWhiteSpace(options.Code))
                return Reject("join 必须带 --code U/XXXX-XXXX-XXXX-XXXX。");
            if (RoomCode.Find(options.Code) == null)
                return Reject("邀请码格式无效：" + options.Code + "（应形如 U/XXXX-XXXX-XXXX-XXXX）");
            return new CliHost().RunJoin(options);
        }

        /// <summary>生成一个合法邀请码，或校验并展开一个邀请码；互通测试时很方便。</summary>
        public static int Code(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(RoomCode.Create().Code);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--check")
            {
                RoomCode code = RoomCode.Find(args[1]);
                if (code == null)
                    return Reject("邀请码无效：" + args[1]);
                Console.WriteLine("邀请码     : " + code.Code);
                Console.WriteLine("虚拟网络名 : " + code.NetworkName);
                Console.WriteLine("网络密钥   : " + code.NetworkSecret);
                return 0;
            }
            return Reject("用法：campfire code  或  campfire code --check <邀请码>");
        }

        public static int Status(string[] args)
        {
            var snapshot = SessionState.Read();
            if (snapshot == null)
            {
                Console.WriteLine("当前没有运行中的会话。");
                Console.WriteLine("数据目录：" + AppSettings.DataDirectory);
                Console.WriteLine("提示：host/join 与 status 必须用同一个 --data-dir。");
                return 1;
            }
            Console.WriteLine("角色     : " + snapshot.Role);
            Console.WriteLine("状态     : " + snapshot.Stage);
            if (!string.IsNullOrEmpty(snapshot.Code))
                Console.WriteLine("邀请码   : " + snapshot.Code);
            if (snapshot.HostPort > 0)
                Console.WriteLine("房主端口 : " + snapshot.HostPort);
            if (snapshot.LocalPort > 0)
                Console.WriteLine("本地端口 : " + snapshot.LocalPort + "  (MC 连 127.0.0.1:" + snapshot.LocalPort + ")");
            Console.WriteLine("节点     : " + snapshot.ConnectedNodes);
            Console.WriteLine("直连/中转: " + snapshot.DirectPeers + " / " + snapshot.RelayedPeers);
            Console.WriteLine("玩家     : " + snapshot.PlayerCount);
            if (!string.IsNullOrEmpty(snapshot.Error))
                Console.WriteLine("错误     : " + snapshot.Error);
            Console.WriteLine("进程 PID : " + snapshot.ProcessId);
            Console.WriteLine("更新时间 : " + DateTimeOffset.FromUnixTimeSeconds(snapshot.UpdatedUnix).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            return snapshot.Stage == "Failed" ? 1 : 0;
        }

        public static int Peers(string[] args)
        {
            var snapshot = SessionState.Read();
            if (snapshot == null)
            {
                Console.WriteLine("当前没有运行中的会话（数据目录：" + AppSettings.DataDirectory + "）。");
                return 1;
            }
            Console.WriteLine("已连接节点 : " + snapshot.ConnectedNodes);
            Console.WriteLine("直连对等   : " + snapshot.DirectPeers);
            Console.WriteLine("经中转     : " + snapshot.RelayedPeers);
            Console.WriteLine("玩家 (" + snapshot.PlayerCount + ") :");
            foreach (string name in snapshot.PlayerNames ?? Array.Empty<string>())
                Console.WriteLine("  - " + name);
            return 0;
        }

        public static int Logs(string[] args)
        {
            int lines = 50;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--lines")
                {
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.None,
                            CultureInfo.InvariantCulture, out lines) || lines <= 0)
                        return Reject("--lines 需要一个正整数。");
                }
                else
                {
                    return Reject("未知参数：" + args[i]);
                }
            }

            string path = LinkLog.FilePath;
            if (!File.Exists(path))
            {
                Console.WriteLine("尚无日志文件：" + path);
                return 1;
            }
            try
            {
                string[] all = File.ReadAllLines(path);
                Console.WriteLine("日志：" + path + "（共 " + all.Length + " 行，显示最后 " + lines + " 行）");
                foreach (string line in all.Skip(Math.Max(0, all.Length - lines)))
                    Console.WriteLine(line);
                return 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("读取日志失败：" + ex.Message);
                return 1;
            }
        }

        public static int Stop(string[] args)
        {
            if (args.Length > 0)
                return Reject("stop 不接受参数。");
            var snapshot = SessionState.Read();
            try
            {
                SessionState.RequestStop();
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            if (snapshot == null)
                Console.WriteLine("已写入停止标记，但没有找到运行中的会话快照。");
            else
                Console.WriteLine("已请求停止 " + snapshot.Role + " 会话 (PID " + snapshot.ProcessId + ")，前台进程会在 1 秒内退出。");
            Console.WriteLine("数据目录：" + AppSettings.DataDirectory + "（须与 host/join 的 --data-dir 一致）");
            return 0;
        }

        private static int Reject(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine("运行 campfire --help 查看用法。");
            return 2;
        }

        private static bool TryParse(string[] args, out SessionOptions options, out string error)
        {
            options = new SessionOptions();
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--port":
                    case "--local-port":
                    case "--timeout":
                    case "--lines":
                        if (i + 1 >= args.Length)
                        {
                            error = arg + " 需要一个整数值。";
                            return false;
                        }
                        string number = args[++i];
                        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0)
                        {
                            error = arg + " 的整数值无效：" + number;
                            return false;
                        }
                        if (arg == "--port") options.Port = value;
                        else if (arg == "--local-port") options.LocalPort = value;
                        else if (arg == "--timeout") options.TimeoutSeconds = value;
                        break;

                    case "--code":
                    case "--player":
                    case "--node":
                        if (i + 1 >= args.Length)
                        {
                            error = arg + " 需要一个字符串值。";
                            return false;
                        }
                        string text = args[++i];
                        if (arg == "--code") options.Code = text;
                        else if (arg == "--player") options.Player = text;
                        else options.Node = text;
                        break;

                    case "--skip-probe": options.SkipProbe = true; break;
                    case "--hide-ip": options.HideIp = true; break;
                    case "--no-relay": options.NoRelay = true; break;
                    case "--share-lan": options.ShareLan = true; break;

                    default:
                        error = "未知参数：" + arg;
                        return false;
                }
            }
            if (options.TimeoutSeconds > 0 && !LinkSettings.SearchTimeoutChoices.Contains(options.TimeoutSeconds))
                error = "--timeout 只能是 30 / 60 / 120 秒。";
            return error == null;
        }
    }
}
