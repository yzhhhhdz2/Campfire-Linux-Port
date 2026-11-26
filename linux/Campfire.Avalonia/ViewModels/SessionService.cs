// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增 GUI 版的长驻会话服务。原 WPF 的 PageLink 是常驻 UserControl，直接持有 LinkSession
//       （LinkPage.xaml.cs:100-101）；这里把同样的职责放进 App 级单例，避免导航重建页面时丢会话。
//       状态字段翻译自 LinkPage.xaml.cs:156-202 与 SessionRuntime.WriteSnapshot；
//       不使用 CliHost.Drive / session.json / stop.requested，改为 Changed 事件驱动。

using Campfire.Avalonia.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using Campfire.Avalonia.Bridge;
using Campfire.Link;

namespace Campfire.Avalonia.ViewModels
{
    public sealed class SessionService : ViewModelBase
    {
        private readonly LinkSession _session;
        private readonly DispatcherTimer _logTimer;

        private string _role = "None";
        private string _stage = "Idle";
        private string _code = "";
        private string _error = "";
        private int _hostPort;
        private int _localPort;
        private int _connectedNodes = -1;
        private int _directPeers;
        private int _relayedPeers;
        private int _playerCount;
        private string _playerNames = "";
        private bool _active;

        public SessionService(SynchronizationContext ui)
        {
            GuiDefaults.Apply();
            _session = new LinkSession(ui);
            // LinkSession 自己会把后台事件 Post 到 ui；这里再兜底一次，确保一定在 UI 线程刷新绑定。
            _session.Changed += (_, __) => Dispatcher.UIThread.Post(Refresh);

            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _logTimer.Tick += (_, __) => RefreshLog();
            _logTimer.Start();

            Refresh();
            RefreshLog();
        }

        public string Role { get => _role; private set => Set(ref _role, value); }
        public string Stage { get => _stage; private set => Set(ref _stage, value); }
        public string Code { get => _code; private set => Set(ref _code, value); }
        public string Error { get => _error; private set => Set(ref _error, value); }
        public int HostPort { get => _hostPort; private set => Set(ref _hostPort, value); }
        public int LocalPort { get => _localPort; private set => Set(ref _localPort, value); }
        public int ConnectedNodes { get => _connectedNodes; private set => Set(ref _connectedNodes, value); }
        public int DirectPeers { get => _directPeers; private set => Set(ref _directPeers, value); }
        public int RelayedPeers { get => _relayedPeers; private set => Set(ref _relayedPeers, value); }
        public int PlayerCount { get => _playerCount; private set => Set(ref _playerCount, value); }
        public string PlayerNames { get => _playerNames; private set => Set(ref _playerNames, value); }
        public string StatusText => _active
            ? Loc.Format("LinkStatusActive", Stage, PlayerCount, DirectPeers, RelayedPeers,
                ConnectedNodes < 0 ? "—" : ConnectedNodes.ToString())
            : (Stage == "Failed" ? Loc.Get("LinkStatusFailed") : Loc.Get("LinkStatusIdle"));

        public bool IsActive => _session.IsActive;
        public bool HasError => !string.IsNullOrEmpty(Error);

        /// <summary>工具页的「日志自动刷新」开关；暂停时不再读文件，恢复后下一拍补上。</summary>
        public bool LogPaused { get; set; }

        /// <summary>加入方是否已拿到通往房主世界的本地转发端口。</summary>
        public bool HasConnection => LocalPort > 0;

        /// <summary>加入方在 Minecraft「多人游戏」里要填的地址；未就绪时给占位文案。</summary>
        public string ConnectionAddress => LocalPort > 0 ? "127.0.0.1:" + LocalPort : Loc.Get("LinkAddressWaiting");

        // 带参数的「标签：值」由 VM 拼好，语言切换时 RaiseAll() 会重新读取。
        public string RoleText => Loc.Format("LinkRole", Role);
        public string StageText => Loc.Format("LinkStage", Stage);
        public string PlayersText => Loc.Format("LinkPlayers", PlayerCount);
        public string PlayerListText => Loc.Format("LinkPlayerList", PlayerNames);
        public string LocalPortText => Loc.Format("LinkLocalPort", LocalPort);

        public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();

        /// <summary>建房。port 传 0 表示让 LinkSession 自己探测（正式联机时用真实 MC 端口）。</summary>
        public void Host(int port)
        {
            GuiDefaults.Apply();
            ClearVisualState();          // 开新房间前先摆脱上一次会话的残留字段
            _session.StartHost(port > 0 ? port : (int?)null, null);
            Dispatcher.UIThread.Post(Refresh);
        }

        /// <summary>加入。返回 false 表示邀请码格式/校验位不对。</summary>
        public bool Join(string text)
        {
            RoomCode code;
            try { code = RoomCode.Find(text ?? ""); }
            catch { code = null; }
            if (code == null)
                return false;

            GuiDefaults.Apply();
            ClearVisualState();          // 同上；也避免加入页闪出上一次建房的邀请码
            _session.StartJoin(code);
            Dispatcher.UIThread.Post(Refresh);
            return true;
        }

        public void Stop()
        {
            _session.Stop();
            ClearVisualState();
            Dispatcher.UIThread.Post(Refresh);
        }

        /// <summary>程序退出时释放；不要每次 Stop 都调，否则无法再次建房。</summary>
        public void Shutdown()
        {
            _logTimer.Stop();
            _session.Stop();
            _session.Shutdown();
        }

        private void Refresh()
        {
            // LinkSession.Stop()（LinkSession.cs:1332-1342）只重置 Role/Stage/Error，
            // 不清 Code/Players/DirectPeers/ConnectedNodes/LocalPort/MinecraftPort。
            // 若这里照抄，Windows 端退出或点「停止」后界面就会留着「玩家 2」和旧邀请码。
            // 因此非活动态一律归零；协议层副本保持逐字不改。
            bool active = _active = _session.IsActive;
            Role = _session.Role.ToString();
            Stage = _session.Stage.ToString();

            if (active)
            {
                Code = _session.Code?.Code ?? "";
                HostPort = _session.MinecraftPort;
                LocalPort = _session.LocalPort;
                ConnectedNodes = _session.ConnectedNodes;
                DirectPeers = _session.DirectPeers;
                RelayedPeers = _session.RelayedPeers;
                var players = _session.Players;
                PlayerCount = players == null ? 0 : players.Count;
                PlayerNames = (players == null || players.Count == 0)
                    ? ""
                    : string.Join(", ", players.Select(p => p.Name));
            }
            else
            {
                ClearVisualState();
            }

            Error = _session.Error?.Message ?? "";
            // StatusText 是计算属性：会话变化时通知一次，语言切换由 RaiseAll 兜底。
            Raise(nameof(StatusText));

            Raise(nameof(IsActive));
            Raise(nameof(HasError));
            Raise(nameof(HasConnection));
            Raise(nameof(ConnectionAddress));
        }

        /// <summary>清空所有「属于某一次会话」的字段，避免停止或开新房间时残留旧值。</summary>
        private void ClearVisualState()
        {
            Code = "";
            Error = "";
            HostPort = 0;
            LocalPort = 0;
            ConnectedNodes = -1;
            DirectPeers = 0;
            RelayedPeers = 0;
            PlayerCount = 0;
            PlayerNames = "";
            Raise(nameof(HasConnection));
            Raise(nameof(ConnectionAddress));
            Raise(nameof(HasError));
        }

        private void RefreshLog()
        {
            if (LogPaused)
                return;
            try
            {
                string path = LinkLog.FilePath;
                if (!File.Exists(path))
                    return;
                string[] tail = File.ReadLines(path).TakeLast(60).ToArray();
                if (LogLines.Count == tail.Length && (tail.Length == 0 || LogLines.LastOrDefault() == tail[tail.Length - 1]))
                    return;
                LogLines.Clear();
                foreach (string line in tail)
                    LogLines.Add(line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
