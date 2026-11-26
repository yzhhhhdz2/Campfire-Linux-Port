// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增第一批 7 个页面 VM。对应原 Pages/ 下的 HomePage / MapsPage / ToolsPage /
//       PreferencesPage / LinkPage / LobbyPage / WorldPage，但只保留 Linux 上真实可用的功能：
//       已裁剪的模组同步、语音、世界备份、地图在线下载、在线更新/统计等开关一律不出现。
//       多个 VM 放在同一文件只为减少工程文件数；ViewLocator 靠类型全名映射，与文件位置无关。

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Campfire.Avalonia.Localization;
using Avalonia.Media;
using Campfire.Avalonia.Bridge;
using Campfire.Avalonia.Themes;
using Campfire.Link;
using Campfire.Link.Lobby;
using Campfire.Link.Runtime;

namespace Campfire.Avalonia.ViewModels
{
    // ============================ 主页 ============================

    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _nav;

        public HomeViewModel(MainWindowViewModel nav)
        {
            _nav = nav;
            Session = nav.Session;
        }

        public SessionService Session { get; }
        public bool HasRoom => Session.IsActive;

        public ICommand CreateRoom => new RelayCommand(() =>
            _nav.OpenSubPage(new LinkViewModel(_nav, true), "HomeCreateRoom"));

        public ICommand JoinRoom => new RelayCommand(() =>
            _nav.OpenSubPage(new LinkViewModel(_nav, false), "HomeJoinRoom"));

        public ICommand OpenLobby => new RelayCommand(() =>
            _nav.OpenSubPage(new LobbyViewModel(_nav), "HomeLobby"));

        // 原版下排两张小磁贴是「最近房间 / 联机设置」。Linux 版没有房间历史记录，
        // 所以换成两个真实存在的入口，避免出现点了没反应的控件。
        public ICommand OpenTools => new RelayCommand(() => _nav.ShowTools.Execute(null));
        public ICommand OpenSettings => new RelayCommand(() => _nav.ShowSettings.Execute(null));
    }

    // ============================ 地图 Tab ============================
    // 用户决定「只删详情子页」：保留 Tab 与导入页，去掉在线下载/搜索，用说明条讲清楚原因。

    public sealed class MapsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _nav;

        public MapsViewModel(MainWindowViewModel nav)
        {
            _nav = nav;
            ScanWorlds();
        }

        public string Notice =>
            Loc.Get("MapsNotice");

        public ObservableCollection<WorldRow> Worlds { get; } = new ObservableCollection<WorldRow>();

        public string WorldsHint => Worlds.Count == 0
            ? Loc.Get("MapsNoWorlds")
            : Loc.Format("MapsWorldsFound", Worlds.Count);

        public ICommand RefreshWorlds => new RelayCommand(() => ScanWorlds());
        public ICommand Import => new RelayCommand(() =>
            _nav.OpenSubPage(new WorldViewModel(), "MapsImport"));

        private void ScanWorlds()
        {
            Worlds.Clear();
            foreach (string saves in WorldScanner.SaveDirectories())
            {
                if (!Directory.Exists(saves))
                    continue;
                foreach (string dir in WorldScanner.SafeDirectories(saves))
                {
                    string level = Path.Combine(dir, "level.dat");
                    Worlds.Add(new WorldRow
                    {
                        Name = Path.GetFileName(dir),
                        Detail = File.Exists(level)
                            ? saves + " · level.dat " + File.GetLastWriteTime(level).ToString("yyyy-MM-dd HH:mm")
                            : saves + " · " + Loc.Get("MapsMissingLevelDat"),
                    });
                }
            }
            Raise(nameof(WorldsHint));
        }
    }

    public sealed class WorldRow
    {
        public string Name { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>只读枚举常见的 Minecraft saves 目录（官方启动器 / Prism / MultiMC / Flatpak）。</summary>
    internal static class WorldScanner
    {
        public static IReadOnlyList<string> SaveDirectories()
        {
            var list = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(xdg))
                xdg = Path.Combine(home, ".local", "share");

            list.Add(Path.Combine(home, ".minecraft", "saves"));

            string[] instanceRoots =
            {
                Path.Combine(xdg, "PrismLauncher", "instances"),
                Path.Combine(home, ".var", "app", "org.prismlauncher.PrismLauncher", "data", "PrismLauncher", "instances"),
                Path.Combine(home, "MultiMC", "instances"),
                Path.Combine(xdg, "MultiMC", "instances"),
            };
            foreach (string root in instanceRoots)
                foreach (string instance in SafeDirectories(root))
                {
                    list.Add(Path.Combine(instance, ".minecraft", "saves"));
                    list.Add(Path.Combine(instance, "minecraft", "saves"));
                }

            return list.Distinct(StringComparer.Ordinal).ToList();
        }

        public static IEnumerable<string> SafeDirectories(string path)
        {
            try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Array.Empty<string>(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }

    public sealed class WorldViewModel : ViewModelBase
    {
        public string Notice =>
            Loc.Get("WorldNotice");

        public string Paths => string.Join("\n", WorldScanner.SaveDirectories().Select(p => "· " + p));
    }

    // ============================ 工具 Tab ============================
    // 用户决定「删工具箱卡片、保留工具 Tab」：换成 4 组只读的真实状态项，不留死控件。

    public sealed class ToolsViewModel : ViewModelBase
    {
        private readonly LobbyService _lobbyService = new LobbyService();
        private string _diagnostics = "";
        private string _component = "";
        private string _lobby = "";
        private string _actionMessage = "";

        public ToolsViewModel(MainWindowViewModel nav)
        {
            Session = nav.Session;
            Refresh();
            _ = ProbeLobbyAsync();
        }

        public SessionService Session { get; }

        public string Diagnostics { get => _diagnostics; private set => Set(ref _diagnostics, value); }
        public string Component { get => _component; private set => Set(ref _component, value); }
        public string Lobby { get => _lobby; private set => Set(ref _lobby, value); }
        public string ActionMessage { get => _actionMessage; private set => Set(ref _actionMessage, value); }
        public ObservableCollection<string> LogLines => Session.LogLines;

        /// <summary>真正暂停/恢复 SessionService 的日志轮询，不是空开关。</summary>
        public bool LogAutoRefresh
        {
            get => !Session.LogPaused;
            set
            {
                Session.LogPaused = !value;
                Raise(nameof(LogAutoRefresh));
            }
        }

        /// <summary>自动开放链路的环境自检；不实际跑 java --list-modules，避免刷新时卡顿。</summary>
        public string Agent
        {
            get
            {
                string[] resources = typeof(ToolsViewModel).Assembly.GetManifestResourceNames();
                bool jars = Array.Exists(resources, n => n.Contains("CampfireAgent.jar"))
                         && Array.Exists(resources, n => n.Contains("CampfireAttach.jar"))
                         && Array.Exists(resources, n => n.Contains("CampfireHook.jar"));
                return Loc.Get("ToolsAgentOn") + (GuiDefaults.AutoLanRequested ? Loc.Get("ToolsAgentRequested") : Loc.Get("ToolsAgentOff")) + "\n"
                     + Loc.Get("ToolsSelftestMode") + (LinkSession.SkipProbe ? Loc.Get("ToolsSkipProbeOn") : Loc.Get("ToolsOff")) + "\n"
                     + "Agent jar    : " + (jars ? Loc.Get("ToolsJarsOk") : Loc.Get("ToolsMissing")) + "\n"
                     + "CAMPFIRE_ATTACH_JAVA : " + (Environment.GetEnvironmentVariable("CAMPFIRE_ATTACH_JAVA") ?? Loc.Get("ToolsNotSet")) + "\n"
                     + "JAVA_HOME    : " + (Environment.GetEnvironmentVariable("JAVA_HOME") ?? Loc.Get("ToolsNotSet")) + "\n"
                     + Loc.Get("ToolsJreNote");
            }
        }

        public ICommand RefreshCommand => new RelayCommand(() => { Refresh(); _ = ProbeLobbyAsync(); });
        public ICommand CheckComponent => new RelayCommand(() => _ = CheckComponentAsync());
        public ICommand StartLobby => new RelayCommand(() =>
        {
            ActionMessage = _lobbyService.Start();
            _ = ProbeLobbyAsync();
        });
        public ICommand StopLobby => new RelayCommand(() =>
        {
            ActionMessage = _lobbyService.Stop();
            _ = ProbeLobbyAsync();
        });

        private async Task CheckComponentAsync()
        {
            if (!EasyTierPackage.IsInstalled())
            {
                ActionMessage = Loc.Get("ToolsCheckNotInstalled");
                return;
            }
            ActionMessage = Loc.Get("ToolsCheckRunning");
            bool ok = await EasyTierPackage.CanRunAsync();
            ActionMessage = ok
                ? Loc.Get("ToolsCheckOk")
                : Loc.Get("ToolsCheckFail");
        }

        private async Task ProbeLobbyAsync()
        {
            string status = await _lobbyService.StatusAsync();
            Lobby =
                Loc.Get("ToolsLabelService") + _lobbyService.Url + "\n" +
                Loc.Get("ToolsLabelLobby") + status + "\n" +
                Loc.Get("ToolsLabelResolver") + (LobbyClient.IsAvailable ? Loc.Get("ToolsResolverConfigured") : Loc.Get("ToolsResolverOff")) + "\n" +
                Loc.Get("ToolsLabelOwnProcess") + (_lobbyService.OwnsProcess ? Loc.Get("ToolsOwnYes") : Loc.Get("ToolsOwnNo"));
        }

        private void Refresh()
        {
            Diagnostics =
                Loc.Get("ToolsLabelRuntime") + Environment.Version + "\n" +
                Loc.Get("ToolsLabelSystem") + Environment.OSVersion.VersionString + "\n" +
                Loc.Get("ToolsLabelSession") + (Environment.GetEnvironmentVariable("DISPLAY") ?? Loc.Get("ToolsEmpty")) +
                "  XDG_SESSION_TYPE=" + (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? Loc.Get("ToolsEmpty")) + "\n" +
                Loc.Get("ToolsLabelDataDir") + LinkSettings.DataDirectory + "\n" +
                Loc.Get("ToolsLabelLogFile") + LinkLog.FilePath + "\n" +
                Loc.Get("ToolsLabelSkipProbe") + (LinkSession.SkipProbe ? Loc.Get("ToolsSkipProbeOnFlag") : Loc.Get("ToolsOff"));

            Component =
                Loc.Get("ToolsLabelEtVersion") + EasyTierPackage.Version + "\n" +
                Loc.Get("ToolsLabelInstall") + (EasyTierPackage.IsInstalled() ? Loc.Get("ToolsInstalled") : Loc.Get("ToolsNotInstalled")) + "\n" +
                Loc.Get("ToolsLabelCorePath") + EasyTierPackage.CorePath;
        }
    }

    // ============================ 设置 Tab ============================
    // 用户决定「壳不动、个性化重建」：132px 侧栏 + 6 分区外观不变，
    // 个性化分区第二批接入完整动态主题，其余分区只放真实可用的项。

    public sealed class PreferencesViewModel : ViewModelBase
    {
        private int _section;
        private string _playerName;
        private int _modeIndex;
        private int _styleIndex;
        private string _customColor = "";
        private string _themeStatus = "";

        public PreferencesViewModel(MainWindowViewModel nav)
        {
            Session = nav.Session;
            _playerName = LinkSettings.PlayerName;
            _modeIndex = (int)ThemeManager.Mode;
            _styleIndex = (int)ThemeManager.Style;
            RebuildSwatches();
        }

        public SessionService Session { get; }

        public int Section { get => _section; private set => Set(ref _section, value); }
        public bool PersonalizeVisible => Section == 0;
        public bool GeneralVisible => Section == 1;
        public bool LinkVisible => Section == 2;
        public bool SupportVisible => Section == 3;
        public bool AboutVisible => Section == 4;
        public bool LicensesVisible => Section == 5;

        public string PlayerName
        {
            get => _playerName;
            set
            {
                if (!Set(ref _playerName, value))
                    return;
                LinkSettings.PlayerName = value;   // 写进 settings.ini，下次建房生效
            }
        }

        public string DataDirectory => LinkSettings.DataDirectory;
        public string VersionText => "1.1.32 Linux port (Avalonia 11 / .NET 8)";
        public string GplText =>
            Loc.Get("GplText");

        // ======================== 第二批：完整动态主题 ========================

        /// <summary>枚举顺序与 ComboBox 项顺序一致：System / Light / Dark。</summary>
        public int ModeIndex
        {
            get => _modeIndex;
            set
            {
                if (!Set(ref _modeIndex, value))
                    return;
                ThemeManager.Mode = (ThemeMode)Math.Max(0, Math.Min(2, value));
                ThemeManager.Apply(true);
                RaiseState();
            }
        }

        /// <summary>枚举顺序与 ComboBox 项顺序一致：TonalSpot / Vibrant / Expressive / Neutral / Monochrome / Fidelity。</summary>
        public int StyleIndex
        {
            get => _styleIndex;
            set
            {
                if (!Set(ref _styleIndex, value))
                    return;
                ThemeManager.Style = (PaletteStyle)Math.Max(0, Math.Min(5, value));
                ThemeManager.Apply(true);
                RaiseState();
            }
        }

        public bool UseWallpaper
        {
            get => ThemeManager.UseWallpaper;
            set
            {
                if (ThemeManager.UseWallpaper == value)
                    return;
                ThemeManager.UseWallpaper = value;
                Raise(nameof(UseWallpaper));
                if (value)
                    _ = RefreshWallpaperAsync();
                else
                {
                    ThemeManager.Apply(true);
                    RaiseState();
                }
            }
        }

        public string CustomColor { get => _customColor; set => Set(ref _customColor, value); }
        public string ThemeStatus { get => _themeStatus; private set => Set(ref _themeStatus, value); }

        public string ThemeState =>
            Loc.Format("ThemeStatus", ThemeManager.SeedHex,
                ThemeManager.IsDark ? Loc.Get("ThemeDark") : Loc.Get("ThemeLight"),
                ThemeManager.Style)
            + (ThemeManager.UseWallpaper ? " · " + Loc.Get("ThemeWallpaper") : "");

        public string SystemThemeHint => ThemeManager.SystemThemeHint;

        public string WallpaperHint => ThemeManager.WallpaperCandidates.Count == 0
            ? Loc.Get("WallpaperNone")
            : Loc.Format("WallpaperFound", ThemeManager.WallpaperCandidates.Count);

        public string ThemeNotice =>
            Loc.Get("ThemeNote");

        public ObservableCollection<ThemeSwatch> WallpaperSwatches { get; } = new ObservableCollection<ThemeSwatch>();

        /// <summary>9 个预设色板与「系统强调色」共用；system 是 ThemeManager 约定的特殊值。</summary>
        public ICommand PickColor => new RelayCommand(parameter =>
        {
            string chosen = parameter as string;
            if (string.IsNullOrEmpty(chosen))
                return;
            ThemeManager.UseWallpaper = false;
            Raise(nameof(UseWallpaper));
            ThemeManager.ChosenColor = chosen;
            ThemeManager.Apply(true);
            ThemeStatus = chosen == "system" ? Loc.Get("ThemeAppliedSystem") : Loc.Format("ThemeApplied", chosen);
            RaiseState();
        });

        public ICommand ApplyCustomColor => new RelayCommand(() =>
        {
            string hex = (CustomColor ?? "").Trim().TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, null, out _))
            {
                ThemeStatus = Loc.Get("ThemeInvalidHex");
                return;
            }
            ThemeManager.UseWallpaper = false;
            Raise(nameof(UseWallpaper));
            ThemeManager.ChosenColor = "#" + hex.ToUpperInvariant();
            ThemeManager.Apply(true);
            ThemeStatus = Loc.Format("ThemeApplied", ThemeManager.SeedHex);
            RaiseState();
        });

        public ICommand RefreshWallpaper => new RelayCommand(() => _ = RefreshWallpaperAsync());

        private async Task RefreshWallpaperAsync()
        {
            await ThemeManager.RefreshWallpaperAsync();
            RebuildSwatches();
            RaiseState();
        }

        private void RebuildSwatches()
        {
            WallpaperSwatches.Clear();
            int index = 0;
            foreach (Color color in ThemeManager.WallpaperCandidates)
            {
                int captured = index++;
                WallpaperSwatches.Add(new ThemeSwatch
                {
                    Brush = new SolidColorBrush(color),
                    Hex = ColorMath.ToHex(color),
                    Pick = new RelayCommand(() =>
                    {
                        ThemeManager.WallpaperChoice = captured;
                        ThemeManager.Apply(true);
                        RaiseState();
                    }),
                });
            }
        }

        private void RaiseState()
        {
            Raise(nameof(ThemeState));
            Raise(nameof(WallpaperHint));
            Raise(nameof(ThemeStatus));
        }

        private ICommand Show(int index) => new RelayCommand(() =>
        {
            Section = index;
            Raise(nameof(PersonalizeVisible));
            Raise(nameof(GeneralVisible));
            Raise(nameof(LinkVisible));
            Raise(nameof(SupportVisible));
            Raise(nameof(AboutVisible));
            Raise(nameof(LicensesVisible));
            if (index == 0)
            {
                // 系统主题轮询可能已在后台改过调色板，切回本分区时刷新一次显示。
                _modeIndex = (int)ThemeManager.Mode;
                _styleIndex = (int)ThemeManager.Style;
                Raise(nameof(ModeIndex));
                Raise(nameof(StyleIndex));
                Raise(nameof(UseWallpaper));
                RaiseState();
            }
        });

        public ICommand ShowPersonalize => Show(0);
        public ICommand ShowGeneral => Show(1);
        public ICommand ShowLink => Show(2);
        public ICommand ShowSupport => Show(3);
        public ICommand ShowAbout => Show(4);
        public ICommand ShowLicenses => Show(5);
    }

    /// <summary>壁纸候选色块；Brush 直接作为 Button.Background，避免 XAML 里做类型转换。</summary>
    public sealed class ThemeSwatch
    {
        public SolidColorBrush Brush { get; set; }
        public string Hex { get; set; }
        public ICommand Pick { get; set; }
    }

    // ============================ 联机房 ============================

    public sealed class LinkViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _nav;
        private readonly bool _hosting;
        private string _port = "43210";
        private string _codeInput = "";
        private string _message = "";
        private bool _autoLan = GuiDefaults.AutoLanRequested;

        public LinkViewModel(MainWindowViewModel nav, bool hosting)
        {
            _nav = nav;
            _hosting = hosting;
            Session = nav.Session;
        }

        public SessionService Session { get; }
        public bool Hosting => _hosting;
        public string RoomTitle => _hosting ? Loc.Get("HomeCreateRoom") : Loc.Get("HomeJoinRoom");
        // 两个页面连按钮文案都不同，避免「除了标题汉字之外没区别」。
        public string StartLabel => _hosting ? Loc.Get("CommonStart") : Loc.Get("HomeJoinRoom");
        public string StopLabel => _hosting ? Loc.Get("CommonStop") : Loc.Get("LinkLeave");

        public string Port { get => _port; set => Set(ref _port, value); }
        public string CodeInput { get => _codeInput; set => Set(ref _codeInput, value); }
        public string Message { get => _message; private set => Set(ref _message, value); }
        public ObservableCollection<string> LogLines => Session.LogLines;

        /// <summary>勾上后由游戏内 Java Agent 自动点「对局域网开放」，不用手填端口。</summary>
        public bool AutoLan
        {
            get => _autoLan;
            set
            {
                if (!Set(ref _autoLan, value))
                    return;
                Raise(nameof(ManualPortEnabled));
            }
        }

        public bool ManualPortEnabled => !AutoLan;

        public string AutoLanHint =>
            Loc.Get("LinkAutoLanHint");

        public ICommand Start => new RelayCommand(() =>
        {
            if (_hosting)
            {
                if (AutoLan)
                {
                    // 自检模式没有真实游戏，WaitForLocalWorldAsync 会一直等端口，必须先挡下。
                    if (LinkSession.SkipProbe)
                    {
                        Message = Loc.Get("LinkMessageSkipProbe");
                        return;
                    }

                    // 让 LinkSession 走 WaitForLocalWorldAsync + OpenWorldToLanAsync：
                    // Java Agent 注入游戏后自动调用 publishServer，端口由 /proc 扫描发现。
                    GuiDefaults.AutoLanRequested = true;
                    Session.Host(0);
                    Message = Loc.Get("LinkMessageWaitGame");
                    return;
                }

                GuiDefaults.AutoLanRequested = false;
                int port;
                if (!int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                    || port <= 0 || port > 65535)
                {
                    Message = Loc.Get("LinkMessageBadPort");
                    return;
                }
                Session.Host(port);
                Message = Loc.Get("LinkMessageHosting");
            }
            else
            {
                if (!Session.Join(CodeInput))
                {
                    Message = Loc.Get("LinkMessageBadCode");
                    return;
                }
                Message = Loc.Get("LinkMessageJoining");
            }
        });

        public ICommand Stop => new RelayCommand(() =>
        {
            Session.Stop();
            Message = _hosting ? Loc.Get("LinkMessageStoppedHost") : Loc.Get("LinkMessageStoppedJoin");
        });
    }

    // ============================ 自建大厅 ============================

    public sealed class RoomRow
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public bool Locked { get; set; }
        /// <summary>由 LobbyViewModel 塞进来，避免 XAML 里写相对源绑定。</summary>
        public ICommand Join { get; set; }
    }

    public sealed class LobbyViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _nav;
        private string _status = "";
        private string _serviceUrl = "";

        public LobbyViewModel(MainWindowViewModel nav)
        {
            _nav = nav;
            _serviceUrl = Campfire.LinuxConfig.ServiceUrl ?? "";
            Refresh();
        }

        public ObservableCollection<RoomRow> Rooms { get; } = new ObservableCollection<RoomRow>();
        public string Status { get => _status; private set => Set(ref _status, value); }
        public string ServiceUrl { get => _serviceUrl; set => Set(ref _serviceUrl, value); }

        public ICommand RefreshCommand => new RelayCommand(() => Refresh());

        public ICommand SaveService => new RelayCommand(() =>
        {
            string error;
            if (!Campfire.LinuxConfig.TrySetServiceUrl(ServiceUrl, out error))
            {
                Status = Loc.Get("LobbyStatusInvalid") + error;
                return;
            }
            Campfire.AppSettings.Set("service.url", ServiceUrl ?? "");
            Status = Loc.Get("LobbyStatusSaved");
        });

        public ICommand JoinRoom => new RelayCommand(async parameter =>
        {
            RoomRow row = parameter as RoomRow;
            if (row == null)
                return;
            try
            {
                Status = Loc.Get("LobbyStatusFetchCode");
                RoomCode code = await LobbyClient.GetCodeAsync(row.Id, null);
                if (code == null)
                {
                    Status = Loc.Get("LobbyStatusLocked");
                    return;
                }
                if (!_nav.Session.Join(code.Code))
                {
                    Status = Loc.Get("LobbyStatusBadCode");
                    return;
                }
                Status = Loc.Get("LobbyStatusJoined");
                _nav.OpenSubPage(new LinkViewModel(_nav, false), "HomeJoinRoom");
            }
            catch (Exception ex)
            {
                Status = Loc.Get("LobbyStatusFail") + ex.Message;
            }
        });

        private async void Refresh()
        {
            if (!LobbyClient.IsAvailable)
            {
                Status = Loc.Get("LobbyStatusUnconfigured");
                Rooms.Clear();
                return;
            }
            try
            {
                Status = Loc.Get("LobbyStatusLoading");
                var list = await LobbyClient.ListAsync();
                Rooms.Clear();
                foreach (var room in list.Rooms)
                {
                    Rooms.Add(new RoomRow
                    {
                        Id = room.Id,
                        Locked = room.Locked,
                        Join = JoinRoom,
                        Text = room.Title + "   " + Loc.Get("LobbyHost") + room.Host + "   "
                             + room.Players + "/" + (room.MaxPlayers > 0 ? room.MaxPlayers.ToString() : "?")
                             + (room.Locked ? Loc.Get("LobbyLocked") : "")
                    });
                }
                Status = Loc.Format("LobbyStatusCount", Rooms.Count, Campfire.LinuxConfig.ServiceUrl ?? "");
            }
            catch (Exception ex)
            {
                Status = Loc.Get("LobbyStatusFail") + ex.Message;
            }
        }
    }
}
