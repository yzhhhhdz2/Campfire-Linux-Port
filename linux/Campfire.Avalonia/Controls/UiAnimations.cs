// Campfire Linux port — 基于 Xocio/Campfire v1.1.32 (GPL-3.0)，新增文件（非改写）。
// 作用：统一的界面动效引擎。第一版只做「卡片淡入 + 状态点呼吸」，本版改成
//       多通道合成 + 通用时间轴，把入场/内容跟进/交互/状态/涟漪/打字机全部收进一处。
//
// 设计约束（重要，别破坏）：
//   · 只改 Opacity 与 RenderTransform（Scale + Translate），**不碰任何随主题变化的 Brush**。
//     ThemeManager 每 20ms 替换 Application.Resources 顶层画刷做 400ms 插值；
//     若给 Background/Foreground 挂过渡，会动画套动画、颜色拖影。
//   · 不触碰 Width/Height/Margin/Padding 等会触发布局重算的属性；软件渲染
//     （Bazzite + llvmpipe）下每帧只做若干次属性赋值。
//   · 所有瞬时动画共享一个 DispatcherTimer（默认 30fps，可配 CAMPFIRE_ANIM_FPS）；
//     没有 Track 也没有 Continuous 项时自动停表。
//   · 通道合成：同一控件上入场、悬停、按压、脉冲各写各的字段，Apply() 统一相乘，
//     所以多条动画叠加不会互相覆盖，快速切页也能一次性收尾到终态。
//   · 全局可关：CAMPFIRE_ANIM=0（完全不接管控件）；=1 强制开；默认在 Linux 上
//     尊重 GNOME 的 org.gnome.desktop.interface enable-animations。
//   · 与 CampfireLogo 自带的 10fps 帧动画、ThemeManager 的 400ms 颜色插值互不干扰。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Campfire.Avalonia.Controls
{
    /// <summary>页面切换方向，由 MainWindowViewModel 在换页前告知，决定入场的横向起点。</summary>
    public enum NavDirection { None, Left, Right, Into, Back }

    public static class UiAnimations
    {
        // ==================== 配置 ====================

        /// <summary>CAMPFIRE_ANIM=0 全关；=1 强制开；否则 Linux 上跟随 GNOME「减少动画」。</summary>
        public static bool Enabled { get; private set; } = ResolveEnabled();

        private static readonly int FrameMs = ResolveFrameMs();
        private const int MaxTracks = 140;   // 上限保护：超出直接跳到终态，不让软件渲染堆积

        private static bool ResolveEnabled()
        {
            string flag = Environment.GetEnvironmentVariable("CAMPFIRE_ANIM");
            if (flag == "0") return false;
            if (flag == "1") return true;
            if (!OperatingSystem.IsLinux()) return true;

            // GNOME 关掉动画时自动降级；gsettings 不存在或超时一律按「开」处理。
            try
            {
                var psi = new ProcessStartInfo(
                    "gsettings", "get org.gnome.desktop.interface enable-animations")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return true;
                    if (!proc.WaitForExit(1200))
                    {
                        try { proc.Kill(); } catch { }
                        return true;
                    }
                    string value = proc.StandardOutput.ReadToEnd().Trim();
                    return !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return true; }
        }

        private static int ResolveFrameMs()
        {
            string fps = Environment.GetEnvironmentVariable("CAMPFIRE_ANIM_FPS");
            if (int.TryParse(fps, out int value) && value >= 15 && value <= 60)
                return 1000 / value;
            return 33;   // ~30fps，软件渲染下的甜点
        }

        // ==================== 缓动 ====================

        private static double Linear(double t) => t;
        private static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);
        private static double QuintOut(double t) => 1 - Math.Pow(1 - t, 5);
        private static double SineInOut(double t) => 0.5 - 0.5 * Math.Cos(Math.PI * t);

        /// <summary>克制的 back-out：过冲约 6%，够有弹性又不浮夸。</summary>
        private static double BackOut(double t)
        {
            const double c1 = 1.15;
            double u = t - 1;
            return 1 + (c1 + 1) * u * u * u + c1 * u * u;
        }

        // ==================== 通道状态 ====================

        private sealed class Channels
        {
            public Control Control;
            public ScaleTransform Scale;
            public TranslateTransform Translate;

            // 各通道独立写入，Apply() 统一相乘 / 相加
            public double EnterOpacity = 1, HoverOpacity = 1, PulseOpacity = 1;
            public double EnterScale = 1, HoverScale = 1, PulseScale = 1;
            public double EnterX, EnterY, HoverY, PulseY, SwayY;

            public void Apply()
            {
                double s = EnterScale * HoverScale * PulseScale;
                if (s < 0.01) s = 0.01;
                Scale.ScaleX = Scale.ScaleY = s;
                Translate.X = EnterX;
                Translate.Y = EnterY + HoverY + PulseY + SwayY;
                double o = EnterOpacity * HoverOpacity * PulseOpacity;
                Control.Opacity = o < 0 ? 0 : (o > 1 ? 1 : o);
            }
        }

        private static readonly Dictionary<Control, Channels> States =
            new Dictionary<Control, Channels>();

        private static Channels State(Control control)
        {
            if (States.TryGetValue(control, out Channels existing))
                return existing;

            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);

            var state = new Channels
            {
                Control = control,
                Scale = scale,
                Translate = translate
            };
            control.RenderTransform = group;
            control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            States[control] = state;
            return state;
        }

        // ==================== 时间轴 ====================

        private sealed class Track
        {
            public double StartMs, DelayMs, DurationMs;
            public Func<double, double> Ease;
            public Action<double> Update;
            public Action Complete;
            public object Owner;
            public string Channel;
        }

        private static readonly List<Track> Tracks = new List<Track>();
        private static readonly List<Action<double>> Continuous = new List<Action<double>>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static DispatcherTimer _timer;

        /// <summary>添加一条动画。update(0) 立即执行，保证 delay 期间保持起始态。</summary>
        private static void Run(double delayMs, double durationMs, Func<double, double> ease,
            Action<double> update, Action complete = null, object owner = null, string channel = null)
        {
            if (!Enabled)
            {
                update(1);
                complete?.Invoke();
                return;
            }
            if (Tracks.Count >= MaxTracks)
            {
                update(1);
                complete?.Invoke();
                return;
            }
            update(0);
            Tracks.Add(new Track
            {
                StartMs = Clock.Elapsed.TotalMilliseconds,
                DelayMs = delayMs,
                DurationMs = Math.Max(1, durationMs),
                Ease = ease,
                Update = update,
                Complete = complete,
                Owner = owner,
                Channel = channel
            });
            EnsureTimer();
        }

        /// <summary>同一控件的同一通道只保留最新一条，避免 hover / press 互相打架。</summary>
        private static void RunExclusive(object owner, string channel, double delayMs,
            double durationMs, Func<double, double> ease, Action<double> update,
            Action complete = null)
        {
            Tracks.RemoveAll(t => t.Owner == owner && t.Channel == channel);
            Run(delayMs, durationMs, ease, update, complete, owner, channel);
        }

        /// <summary>
        /// 换页前只把「入场」通道推到终态，卡片不会卡在半透明。
        /// 窗口开场 / 光环 / 涟漪 / 打字机 / 悬停都不受换页影响，否则首页上树时
        /// 会把 420ms 的窗口开场直接掐断。
        /// </summary>
        private static void FinishAll()
        {
            Track[] snapshot = Tracks.Where(t => t.Channel == "enter").ToArray();
            if (snapshot.Length == 0) return;
            foreach (Track track in snapshot)
                Tracks.Remove(track);
            foreach (Track track in snapshot)
            {
                track.Update(1);
                track.Complete?.Invoke();
            }
        }

        private static void EnsureTimer()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
            _timer.Tick += Tick;
            _timer.Start();
        }

        private static void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= Tick;
            _timer = null;
        }

        private static void Tick(object sender, EventArgs e)
        {
            double now = Clock.Elapsed.TotalMilliseconds;

            // 倒序遍历：Complete 回调里再 Add 新 Track 也不会打乱本轮
            for (int i = Tracks.Count - 1; i >= 0; i--)
            {
                Track track = Tracks[i];
                double p = (now - track.StartMs - track.DelayMs) / track.DurationMs;
                if (p < 0) continue;
                if (p >= 1)
                {
                    track.Update(1);
                    Tracks.RemoveAt(i);
                    track.Complete?.Invoke();
                    continue;
                }
                track.Update(track.Ease(p));
            }

            if (Continuous.Count > 0)
            {
                double seconds = now / 1000.0;
                Action<double>[] items = Continuous.ToArray();
                foreach (Action<double> action in items)
                    action(seconds);
            }

            if (Tracks.Count == 0 && Continuous.Count == 0)
                StopTimer();
        }

        private static void Cleanup(Control root)
        {
            Control[] dead = States.Keys
                .Where(c => c == root || c.GetVisualAncestors().Contains(root))
                .ToArray();
            foreach (Control control in dead)
            {
                States.Remove(control);
                Hooked.Remove(control);
            }
            Entered.Remove(root);
            // 已卸载控件上的残留 Track 直接收尾，别让它们白跑几百毫秒
            Tracks.RemoveAll(t => t.Owner is Control c && dead.Contains(c));
        }

        // ==================== 导航方向 ====================

        private static NavDirection _nav = NavDirection.None;

        public static void NoteNavigation(NavDirection direction) { _nav = direction; }

        private static NavDirection TakeNavigation()
        {
            NavDirection value = _nav;
            _nav = NavDirection.None;
            return value;
        }

        // ==================== 窗口 / 页面入场 ====================

        /// <summary>窗口主体的开场：淡入 + 极轻放大 + 上浮。施加在最外层 PanRoot，阴影一起动。</summary>
        public static void AttachWindowIntro(Control panRoot)
        {
            if (!Enabled || panRoot == null) return;
            Channels s = State(panRoot);
            Run(0, 420, QuintOut, k =>
            {
                s.EnterOpacity = k;
                s.EnterScale = 0.972 + 0.028 * k;
                s.EnterY = 8 * (1 - k);
                s.Apply();
            }, () =>
            {
                s.EnterOpacity = 1; s.EnterScale = 1; s.EnterY = 0; s.Apply();
            }, panRoot, "intro");
        }

        /// <summary>View 构造时调用；每次上树（切 Tab / 进子页 / 返回）都会重播入场。</summary>
        public static void AttachEnter(Control root)
        {
            if (root == null) return;
            if (!Enabled) return;

            root.AttachedToVisualTree += (_, __) =>
                Dispatcher.UIThread.Post(() =>
                {
                    EnterPage(root);
                    AttachInteractions(root);
                    AttachLiveText(root);
                }, DispatcherPriority.Loaded);
            root.DetachedFromVisualTree += (_, __) => Cleanup(root);
        }

        // 同一 View 实例只入场一次：Avalonia 换 Content 时 AttachedToVisualTree
        // 可能触发两回，若重播会把入场时间拉长一倍，看起来像「卡片迟迟不出现」。
        private static readonly HashSet<Control> Entered = new HashSet<Control>();

        private static void EnterPage(Control root)
        {
            if (!Entered.Add(root)) return;
            FinishAll();

            NavDirection nav = TakeNavigation();
            double fromX;
            switch (nav)
            {
                case NavDirection.Into: fromX = 30; break;
                case NavDirection.Back: fromX = -30; break;
                case NavDirection.Left: fromX = -16; break;
                case NavDirection.Right: fromX = 16; break;
                default: fromX = 0; break;
            }

            Border[] cards = root.GetVisualDescendants()
                .OfType<Border>()
                .Where(b => (b.Classes.Contains("pcl-card") || b.Classes.Contains("pcl-hint"))
                            && b.IsVisible)
                .Take(14)
                .ToArray();

            if (cards.Length == 0)
            {
                EnterCard(root, 0, fromX, 300);
                return;
            }

            for (int i = 0; i < cards.Length; i++)
            {
                Border card = cards[i];
                double delay = i * 46;
                EnterCard(card, delay, fromX, 300);

                // 卡片内部内容再错峰跟进：标题 → 说明 → 控件，一层层浮出来
                Panel panel = card.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
                if (panel == null) continue;
                int count = Math.Min(panel.Children.Count, 5);
                for (int j = 0; j < count; j++)
                {
                    if (!(panel.Children[j] is Control item) || !item.IsVisible) continue;
                    Channels si = State(item);
                    double itemDelay = delay + 96 + j * 26;
                    Run(itemDelay, 220, CubicOut, k =>
                    {
                        si.EnterOpacity = k;
                        si.EnterY = 6 * (1 - k);
                        si.Apply();
                    }, () =>
                    {
                        si.EnterOpacity = 1; si.EnterY = 0; si.Apply();
                    }, item, "enter");
                }
            }
        }

        private static void EnterCard(Control card, double delay, double fromX, double duration)
        {
            Channels s = State(card);
            Run(delay, duration, QuintOut, k =>
            {
                s.EnterOpacity = Math.Min(1, k * 1.6);          // 前 62% 完成淡入
                s.EnterY = 18 * (1 - BackOut(k));               // 轻微过冲回弹
                s.EnterX = fromX * (1 - k);
                s.EnterScale = 0.965 + 0.035 * k;
                s.Apply();
            }, () =>
            {
                s.EnterOpacity = 1; s.EnterY = 0; s.EnterX = 0;
                s.EnterScale = 1; s.Apply();
            }, card, "enter");
        }

        // ==================== 状态点 / 营火微动 ====================

        private static Ellipse _dot, _ring;
        private static bool _active, _wasReady;

        /// <summary>标题栏空心圆点：空闲慢呼吸、连接中快脉冲、Ready 常亮；转入 Ready 放一圈光环。</summary>
        public static void AddStatusDot(Ellipse dot, Ellipse ring)
        {
            _dot = dot;
            _ring = ring;
            if (!Enabled)
            {
                if (dot != null) dot.Opacity = 1;
                return;
            }
            if (ring != null)
            {
                Channels s = State(ring);
                s.EnterOpacity = 0;
                s.Apply();
            }
            if (!_continuousAdded)
            {
                _continuousAdded = true;
                Continuous.Add(ContinuousTick);
            }
            EnsureTimer();
        }

        private static bool _continuousAdded;
        private static Control _swayTarget;

        /// <summary>营火帧动画之上叠一层几乎看不见的摇曳，让它不是机械平移。</summary>
        public static void AddSway(Control logo)
        {
            if (!Enabled || logo == null) return;
            _swayTarget = logo;
            State(logo);            // 注册通道，ContinuousTick 才会驱动它
            if (!_continuousAdded)
            {
                _continuousAdded = true;
                Continuous.Add(ContinuousTick);
            }
            EnsureTimer();
        }

        public static void SetConnectionState(bool active, bool ready)
        {
            _active = active;
            if (ready && !_wasReady)
                PulseRing();
            _wasReady = ready;
        }

        private static void ContinuousTick(double seconds)
        {
            if (_dot != null && _dot.IsVisible)
            {
                if (_wasReady)
                {
                    _dot.Opacity = 1;
                }
                else
                {
                    double period = _active ? 0.95 : 2.8;
                    double amp = _active ? 0.42 : 0.24;
                    double mid = _active ? 0.58 : 0.62;
                    _dot.Opacity = mid + amp * Math.Sin(seconds / period * Math.PI * 2);
                }
            }
            if (_swayTarget != null && States.ContainsKey(_swayTarget))
            {
                Channels s = States[_swayTarget];
                s.SwayY = Math.Sin(seconds / 3.1 * Math.PI * 2) * 0.7;
                s.Apply();
            }
        }

        private static void PulseRing()
        {
            if (_ring == null || !Enabled) return;
            Channels s = State(_ring);
            Run(0, 620, CubicOut, k =>
            {
                s.EnterScale = 0.6 + 2.2 * k;
                s.EnterOpacity = 0.75 * (1 - k);
                s.Apply();
            }, () =>
            {
                s.EnterScale = 1; s.EnterOpacity = 0; s.Apply();
            }, _ring, "ring");
        }

        // ==================== 交互：按钮 / 卡片 ====================

        private static readonly HashSet<Control> Hooked = new HashSet<Control>();
        private static Canvas _fxLayer;

        /// <summary>窗口顶层 Canvas，用于绘制不参与布局、不拦截点击的涟漪。</summary>
        public static void SetFxLayer(Canvas canvas) { _fxLayer = canvas; }

        private static void AttachInteractions(Control root)
        {
            Button[] buttons = root.GetVisualDescendants().OfType<Button>().ToArray();
            foreach (Button button in buttons)
                AttachButton(button);

            foreach (Border card in root.GetVisualDescendants().OfType<Border>()
                         .Where(b => b.Classes.Contains("pcl-card") || b.Classes.Contains("pcl-hint")))
                AttachCardHover(card);
        }

        private static void AttachButton(Button button)
        {
            if (!Hooked.Add(button)) return;

            bool swatch = button.Classes.Contains("pcl-swatch");
            bool tile = button.Classes.Contains("pcl-tile");
            bool titlebar = button.Classes.Contains("pcl-titlebar");
            double hoverScale = swatch ? 1.15 : titlebar ? 1.10 : tile ? 1.018 : 1.035;
            double hoverLift = tile ? -2.5 : 0;
            bool hovered = false;
            bool pressed = false;

            button.PointerEntered += (_, __) =>
            {
                hovered = true;
                AnimateHover(button, hoverScale, hoverLift, 190);
            };
            button.PointerExited += (_, __) =>
            {
                hovered = false;
                pressed = false;
                AnimateHover(button, 1, 0, 220);
            };
            button.PointerPressed += (_, e) =>
            {
                pressed = true;
                AnimateHover(button, 0.945, 0, 90);
                Ripple(button, e.GetPosition(button));
            };
            button.PointerReleased += (_, __) =>
            {
                if (!pressed) return;
                pressed = false;
                Channels s = State(button);
                double targetScale = hovered ? hoverScale : 1;
                double targetLift = hovered ? hoverLift : 0;
                double fromScale = s.HoverScale;
                RunExclusive(button, "hover", 0, 300, BackOut, k =>
                {
                    s.HoverScale = fromScale + (targetScale - fromScale) * k;
                    s.HoverY = targetLift * k;
                    s.Apply();
                }, () =>
                {
                    s.HoverScale = targetScale; s.HoverY = targetLift; s.Apply();
                });
                if (swatch)
                    Pulse(button, 1.20, 340);
            };
        }

        private static void AnimateHover(Control control, double targetScale, double targetLift,
            double duration)
        {
            Channels s = State(control);
            double fromScale = s.HoverScale;
            double fromLift = s.HoverY;
            RunExclusive(control, "hover", 0, duration, CubicOut, k =>
            {
                s.HoverScale = fromScale + (targetScale - fromScale) * k;
                s.HoverY = fromLift + (targetLift - fromLift) * k;
                s.Apply();
            }, () =>
            {
                s.HoverScale = targetScale; s.HoverY = targetLift; s.Apply();
            });
        }

        private static void AttachCardHover(Border card)
        {
            if (!Hooked.Add(card)) return;
            card.PointerEntered += (_, __) => AnimateHover(card, 1.004, -1.5, 200);
            card.PointerExited += (_, __) => AnimateHover(card, 1, 0, 240);
        }

        /// <summary>数值/色板的一次性弹性脉冲，走独立通道，不会打断入场或悬停。</summary>
        public static void Pulse(Control control, double peak, double durationMs)
        {
            if (!Enabled || control == null) return;
            Channels s = State(control);
            RunExclusive(control, "pulse", 0, durationMs, Linear, k =>
            {
                double bump = Math.Sin(Math.PI * k);
                s.PulseScale = 1 + (peak - 1) * bump;
                s.PulseY = -2.5 * bump;              // 轻轻浮起再落回
                s.Apply();
            }, () =>
            {
                s.PulseScale = 1; s.PulseY = 0; s.Apply();
            });
        }

        private static void Ripple(Control source, Point local)
        {
            Canvas layer = _fxLayer;
            if (layer == null || layer.Children.Count > 14) return;

            Point? center;
            try { center = source.TranslatePoint(local, layer); }
            catch { return; }
            if (center == null) return;

            double radius = Math.Max(26,
                Math.Min(150, Math.Max(source.Bounds.Width, source.Bounds.Height) * 1.15));
            Color color = AccentColor(layer);

            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(color),
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            };
            var scale = new ScaleTransform(0.08, 0.08);
            ellipse.RenderTransform = scale;
            Canvas.SetLeft(ellipse, center.Value.X - radius);
            Canvas.SetTop(ellipse, center.Value.Y - radius);
            layer.Children.Add(ellipse);

            Run(0, 480, QuintOut, k =>
            {
                double v = 0.08 + 1.18 * k;
                scale.ScaleX = scale.ScaleY = v;
                ellipse.Opacity = 0.28 * (1 - k);
            }, () => layer.Children.Remove(ellipse), ellipse, "ripple");
        }

        private static Color AccentColor(Visual host)
        {
            try
            {
                if (host.TryFindResource("Brush3", out object resource)
                    && resource is SolidColorBrush brush)
                    return brush.Color;
            }
            catch { }
            return Color.FromRgb(0xE2, 0x68, 0x0C);   // 兜底：篝火橙
        }

        // ==================== 数值变化脉冲 ====================

        /// <summary>Classes 含 pcl-live 的文本在内容变化时弹一下，给「状态刷新」可见反馈。</summary>
        private static void AttachLiveText(Control root)
        {
            foreach (TextBlock text in root.GetVisualDescendants().OfType<TextBlock>()
                         .Where(t => t.Classes.Contains("pcl-live")))
            {
                if (!Hooked.Add(text)) continue;
                string last = text.Text;
                text.PropertyChanged += (_, e) =>
                {
                    if (e.Property != TextBlock.TextProperty) return;
                    string now = text.Text;
                    if (string.Equals(now, last, StringComparison.Ordinal)) return;
                    last = now;
                    if (!Enabled || string.IsNullOrEmpty(now)) return;
                    Pulse(text, 1.10, 260);
                };
            }
        }

        // ==================== 邀请码打字机 ====================

        /// <summary>
        /// 把邀请码逐字打出来。关掉动画时仍订阅并直接显示全文（不能因此丢内容）。
        /// 该 TextBlock 不再用 Binding，避免绑定值与打字中的中间值互相覆盖。
        /// </summary>
        public static void AttachTypewriter(TextBlock target, INotifyPropertyChanged source,
            string propertyName, Func<string> read)
        {
            if (target == null || source == null) return;
            string current = read() ?? "";
            target.Text = current;

            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != propertyName) return;
                string text = read() ?? "";
                if (string.Equals(text, current, StringComparison.Ordinal)) return;
                current = text;
                if (!Enabled || text.Length == 0)
                {
                    target.Text = text;
                    return;
                }
                int length = text.Length;
                double duration = Math.Max(180, length * 26);
                RunExclusive(target, "type", 0, duration, Linear, k =>
                {
                    int n = Math.Min(length, (int)Math.Ceiling(length * k));
                    target.Text = text.Substring(0, n);
                }, () => target.Text = text);
                Pulse(target, 1.06, 300);
            };
        }
    }
}
