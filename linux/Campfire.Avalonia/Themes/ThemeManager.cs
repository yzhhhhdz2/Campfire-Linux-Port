// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：从 src/Campfire/Themes/ThemeManager.cs 移植完整动态主题。
//   · ThemeMode / PaletteStyle / 9 个预设 / Palette.Create / FillLight / FillDark / SetRamp / SetGrays
//     全部逐字移植（只把 System.Windows.Media.Color 换成 Avalonia.Media.Color）。
//   · 设置持久化直接复用 Linux 版 AppSettings.Get/Set（签名与原版一致），键名不变。
//   · WPF 的 BeginAnimation(ColorAnimation, 400ms CubicEase) 在 Avalonia 无等价物 →
//     改成 DispatcherTimer 20ms 逐帧插值，每帧替换 Application.Resources 顶层条目，
//     {DynamicResource} 随之刷新。SystemEvents.UserPreferenceChanged → System.Threading.Timer 3s 轮询。
//   · Registry AppsUseLightTheme / DwmGetColorizationColor → LinuxThemeSources。
//   · 差异与降级顺序见 linux/README.md「动态主题」章节。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Campfire.Avalonia.Localization;

namespace Campfire.Avalonia.Themes
{
    internal enum ThemeMode
    {
        System,
        Light,
        Dark,
    }

    /// <summary>How the palette is derived from the key color.</summary>
    internal enum PaletteStyle
    {
        TonalSpot,
        Vibrant,
        Expressive,
        Neutral,
        Monochrome,
        Fidelity,
    }

    /// <summary>
    /// Builds every color of the interface from one key color and puts them into the application resources,
    /// where every {DynamicResource} picks them up at once.
    /// </summary>
    internal static class ThemeManager
    {
        public static readonly Color DefaultSeed = Color.FromRgb(0xE2, 0x68, 0x0C);

        /// <summary>Ready-made key colors offered next to the default.</summary>
        public static readonly Color[] Presets =
        {
            Color.FromRgb(0xE2, 0x68, 0x0C), // campfire orange
            Color.FromRgb(0xD9, 0x3A, 0x3A), // red
            Color.FromRgb(0xD9, 0x4A, 0x8C), // pink
            Color.FromRgb(0x8E, 0x5B, 0xD6), // purple
            Color.FromRgb(0x3D, 0x6F, 0xE0), // blue
            Color.FromRgb(0x14, 0x9C, 0xB5), // teal
            Color.FromRgb(0x2E, 0x9E, 0x5B), // green
            Color.FromRgb(0xC9, 0x9A, 0x06), // gold
            Color.FromRgb(0x8D, 0x6E, 0x5A), // brown
        };

        /// <summary>Raised on the UI thread after the colors changed.</summary>
        public static event EventHandler Changed;

        private static IDictionary<object, object> _resources;
        private static Timer _systemTimer;         // 线程池轮询系统深浅，避免 gsettings 卡 UI
        private static bool _lastSystemDark;
        private static int _polling;

        // ======================== Settings ========================

        public static ThemeMode Mode
        {
            get
            {
                switch (AppSettings.Get("theme.mode"))
                {
                    case "light": return ThemeMode.Light;
                    case "dark": return ThemeMode.Dark;
                    default: return ThemeMode.System;
                }
            }
            set => AppSettings.Set("theme.mode", value == ThemeMode.Light ? "light" : value == ThemeMode.Dark ? "dark" : null);
        }

        /// <summary>Whether the key color comes from the desktop wallpaper.</summary>
        public static bool UseWallpaper
        {
            get => AppSettings.Get("theme.monet") == "1";
            set => AppSettings.Set("theme.monet", value ? "1" : null);
        }

        /// <summary>Which of the wallpaper's colors is used.</summary>
        public static int WallpaperChoice
        {
            get => int.TryParse(AppSettings.Get("theme.monet.index"), out int index) && index >= 0 ? index : 0;
            set => AppSettings.Set("theme.monet.index", value > 0 ? value.ToString() : null);
        }

        /// <summary>The chosen key color as "#RRGGBB", or "system" for the GNOME accent color.</summary>
        public static string ChosenColor
        {
            get => AppSettings.Get("theme.color") ?? ColorMath.ToHex(DefaultSeed);
            set => AppSettings.Set("theme.color", value);
        }

        public static PaletteStyle Style
        {
            get => Enum.TryParse(AppSettings.Get("theme.style"), out PaletteStyle style) ? style : PaletteStyle.TonalSpot;
            set => AppSettings.Set("theme.style", value == PaletteStyle.TonalSpot ? null : value.ToString());
        }

        // ======================== State ========================

        public static bool IsDark { get; private set; }

        /// <summary>The key color in effect.</summary>
        public static Color Seed { get; private set; } = DefaultSeed;

        /// <summary>Colors found in the wallpaper, best first; may be empty.</summary>
        public static IReadOnlyList<Color> WallpaperCandidates { get; private set; } = Array.Empty<Color>();

        /// <summary>当前生效的种子色，供设置页显示。</summary>
        public static string SeedHex => ColorMath.ToHex(Seed);

        /// <summary>系统深浅来源说明（三语文案统一走 Localization/Strings.cs）。</summary>
        public static string SystemThemeHint => Loc.Get("PrefsModeNote");

        public static void Initialize()
        {
            _resources = Application.Current?.Resources;
            if (UseWallpaper)
                WallpaperCandidates = LinuxThemeSources.Extract();
            Apply(false);

            _lastSystemDark = LinuxThemeSources.SystemPrefersDark();
            // Linux 没有 SystemEvents.UserPreferenceChanged；用后台线程轮询，600ms 去抖由「值变化才 Apply」代替。
            _systemTimer = new Timer(_ => PollSystem(), null, 3000, 3000);
        }

        public static void Shutdown()
        {
            _systemTimer?.Dispose();
            _systemTimer = null;
            StopAnimation();
        }

        private static void PollSystem()
        {
            if (Interlocked.Exchange(ref _polling, 1) != 0)
                return;
            try
            {
                bool dark = LinuxThemeSources.SystemPrefersDark();
                Dispatcher.UIThread.Post(() =>
                {
                    if (dark == _lastSystemDark)
                        return;
                    _lastSystemDark = dark;
                    if (Mode == ThemeMode.System)
                        Apply(true);
                });
            }
            catch
            {
                // 取不到系统主题时保持现状，不影响运行。
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        /// <summary>Reads the wallpaper again; the work happens in the background.</summary>
        public static async Task RefreshWallpaperAsync()
        {
            WallpaperCandidates = await Task.Run(() => LinuxThemeSources.Extract());
            Apply(true);
        }

        /// <summary>Recomputes the palette from the current settings.</summary>
        /// <param name="animate">Whether every color fades from its old value to the new one.</param>
        public static void Apply(bool animate)
        {
            if (_resources == null)
                _resources = Application.Current?.Resources;
            if (_resources == null)
                return;

            IsDark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && LinuxThemeSources.SystemPrefersDark());
            Seed = ResolveSeed();
            Palette palette = Palette.Create(Seed, Style, IsDark);

            StopAnimation();
            var solidAnims = new List<(string Key, Color From, Color To)>();
            var gradientAnims = new List<(string Key, Color[] From, Color[] To)>();
            var colorAnims = new List<(string Key, Color From, Color To)>();

            foreach (var entry in palette.Solids)
            {
                Color? from = _resources.TryGetValue(entry.Key, out object current) && current is SolidColorBrush brush
                    ? brush.Color : (Color?)null;
                if (animate && from.HasValue && from.Value != entry.Value)
                    solidAnims.Add((entry.Key, from.Value, entry.Value));
                else
                    _resources[entry.Key] = new SolidColorBrush(entry.Value);
            }

            foreach (var entry in palette.Gradients)
            {
                Color[] from = _resources.TryGetValue(entry.Key, out object current)
                    && current is LinearGradientBrush old && old.GradientStops.Count == entry.Value.Length
                    ? old.GradientStops.Select(s => s.Color).ToArray()
                    : null;
                if (animate && from != null && !from.SequenceEqual(entry.Value))
                    gradientAnims.Add((entry.Key, from, entry.Value));
                else
                    _resources[entry.Key] = MakeGradient(entry.Key, entry.Value);
            }

            foreach (var entry in palette.Colors)
            {
                Color? from = _resources.TryGetValue(entry.Key, out object current) && current is Color c
                    ? c : (Color?)null;
                if (animate && from.HasValue && from.Value != entry.Value)
                    colorAnims.Add((entry.Key, from.Value, entry.Value));
                else
                    _resources[entry.Key] = entry.Value;
            }

            if (solidAnims.Count > 0 || gradientAnims.Count > 0 || colorAnims.Count > 0)
                StartAnimation(solidAnims, gradientAnims, colorAnims);

            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static Color ResolveSeed()
        {
            if (UseWallpaper)
            {
                if (WallpaperCandidates.Count > 0)
                    return WallpaperCandidates[Math.Min(WallpaperChoice, WallpaperCandidates.Count - 1)];
                return LinuxThemeSources.SystemAccent() ?? DefaultSeed;
            }
            string chosen = ChosenColor;
            if (chosen == "system")
                return LinuxThemeSources.SystemAccent() ?? DefaultSeed;
            return ColorMath.Parse(chosen, DefaultSeed);
        }

        // ======================== 400ms 过渡（替代 WPF BeginAnimation） ========================

        private const double TransitionMs = 400;
        private static DispatcherTimer _animTimer;
        private static DateTime _animStart;
        private static List<(string Key, Color From, Color To)> _solidAnims;
        private static List<(string Key, Color[] From, Color[] To)> _gradientAnims;
        private static List<(string Key, Color From, Color To)> _colorAnims;

        private static void StartAnimation(
            List<(string Key, Color From, Color To)> solids,
            List<(string Key, Color[] From, Color[] To)> gradients,
            List<(string Key, Color From, Color To)> colors)
        {
            _solidAnims = solids;
            _gradientAnims = gradients;
            _colorAnims = colors;
            _animStart = DateTime.UtcNow;
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();
        }

        private static void StopAnimation()
        {
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimationTick;
                _animTimer = null;
            }
            _solidAnims = null;
            _gradientAnims = null;
            _colorAnims = null;
        }

        private static void OnAnimationTick(object sender, EventArgs e)
        {
            if (_solidAnims == null)
                return;
            double t = (DateTime.UtcNow - _animStart).TotalMilliseconds / TransitionMs;
            bool done = t >= 1;
            if (done)
                t = 1;
            double k = EaseInOutCubic(t);

            foreach (var item in _solidAnims)
                _resources[item.Key] = new SolidColorBrush(Lerp(item.From, item.To, k));
            foreach (var item in _gradientAnims)
                _resources[item.Key] = MakeGradient(item.Key, LerpStops(item.From, item.To, k));
            foreach (var item in _colorAnims)
                _resources[item.Key] = Lerp(item.From, item.To, k);

            if (done)
            {
                foreach (var item in _solidAnims)
                    _resources[item.Key] = new SolidColorBrush(item.To);
                foreach (var item in _gradientAnims)
                    _resources[item.Key] = MakeGradient(item.Key, item.To);
                foreach (var item in _colorAnims)
                    _resources[item.Key] = item.To;
                StopAnimation();
            }
        }

        private static double EaseInOutCubic(double t)
            => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

        private static Color Lerp(Color from, Color to, double k)
        {
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * k);
            return Color.FromArgb(Mix(from.A, to.A), Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
        }

        private static Color[] LerpStops(Color[] from, Color[] to, double k)
        {
            var result = new Color[to.Length];
            for (int i = 0; i < to.Length; i++)
                result[i] = Lerp(from[i], to[i], k);
            return result;
        }

        private static LinearGradientBrush MakeGradient(string key, Color[] colors)
        {
            // 与原版一致：标题栏横向渐变，其余为斜向渐变。
            var brush = key == "BrushTitleBar"
                ? new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                }
                : new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.1, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.9, 1, RelativeUnit.Relative),
                };
            for (int i = 0; i < colors.Length; i++)
                brush.GradientStops.Add(new GradientStop(colors[i], i / (double)(colors.Length - 1)));
            return brush;
        }

        // ======================== Palette（逐字移植） ========================

        /// <summary>Every themed color of the interface, worked out from a key color.</summary>
        private sealed class Palette
        {
            public Dictionary<string, Color> Solids { get; } = new Dictionary<string, Color>();

            public Dictionary<string, Color[]> Gradients { get; } = new Dictionary<string, Color[]>();

            public Dictionary<string, Color> Colors { get; } = new Dictionary<string, Color>();

            public static Palette Create(Color seedColor, PaletteStyle style, bool dark)
            {
                var seed = Oklch.FromColor(seedColor);
                double hue = seed.H;
                double accent, neutral;
                switch (style)
                {
                    case PaletteStyle.Vibrant:
                        accent = 0.22;
                        neutral = 0.02;
                        break;
                    case PaletteStyle.Expressive:
                        hue -= 60;
                        accent = 0.15;
                        neutral = 0.018;
                        break;
                    case PaletteStyle.Neutral:
                        accent = 0.045;
                        neutral = 0.008;
                        break;
                    case PaletteStyle.Monochrome:
                        accent = 0;
                        neutral = 0;
                        break;
                    case PaletteStyle.Fidelity:
                        accent = Math.Min(seed.C, 0.26);
                        neutral = Math.Min(seed.C * 0.1, 0.016);
                        break;
                    default:
                        accent = Math.Max(0.10, Math.Min(seed.C, 0.16));
                        neutral = 0.014;
                        break;
                }
                // A gray key color stays gray.
                if (seed.C < 0.02 && style != PaletteStyle.Vibrant && style != PaletteStyle.Expressive)
                {
                    accent = Math.Min(accent, 0.02);
                    neutral = Math.Min(neutral, 0.004);
                }

                var palette = new Palette();
                if (dark)
                    palette.FillDark(hue, accent, neutral);
                else
                    palette.FillLight(hue, accent, neutral, style == PaletteStyle.Fidelity ? seed : (Oklch?)null);
                return palette;
            }

            private void FillLight(double hue, double accent, double neutral, Oklch? exact)
            {
                Color A(double l, double c, byte alpha = 255) => new Oklch(l, c, hue).ToColor(alpha);
                Color N(double l, double factor, byte alpha = 255) => new Oklch(l, neutral * factor, hue).ToColor(alpha);

                var primary = exact.HasValue
                    ? new Oklch(Math.Max(0.5, Math.Min(0.68, exact.Value.L)), accent, hue)
                    : new Oklch(0.645, accent, hue);
                SetRamp(
                    N(0.34, 2.2),
                    A(primary.L - 0.095, accent),
                    primary.ToColor(),
                    A(0.73, accent * 0.8),
                    A(0.84, accent * 0.45),
                    A(0.925, Math.Min(0.045, accent * 0.3)),
                    A(0.950, Math.Min(0.032, accent * 0.22)),
                    A(0.972, Math.Min(0.020, accent * 0.14)));
                SetGrays(N(0.38, 0.6), N(0.52, 0.6), N(0.60, 0.6), N(0.72, 0.6), N(0.89, 0.6));

                Solids["BrushOnAccent"] = global::Avalonia.Media.Colors.White;
                Solids["BrushTitleAccent"] = primary.ToColor();
                Solids["BrushSurface"] = global::Avalonia.Media.Colors.White;
                Solids["BrushCard"] = N(0.995, 0.3, 0xCC);
                Solids["BrushCardHover"] = N(0.995, 0.3, 0xEB);
                Solids["BrushTile"] = N(0.995, 0.3, 0x9E);
                Solids["BrushInput"] = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
                Solids["BrushInputFocus"] = global::Avalonia.Media.Colors.White;
                Solids["BrushGreen"] = Color.FromRgb(0x1E, 0x9E, 0x58);
                Solids["BrushGreenLight"] = Color.FromRgb(0xE3, 0xF5, 0xEA);
                Solids["BrushRed"] = Color.FromRgb(0xD8, 0x29, 0x29);
                Solids["BrushRedLight"] = Color.FromRgb(0xFD, 0xE8, 0xE8);
                Solids["BrushOrange"] = Color.FromRgb(0xB8, 0x62, 0x00);
                Solids["BrushOrangeLight"] = Color.FromRgb(0xFD, 0xF1, 0xE0);
                SetLampColors();
                Solids["BrushDialogMask"] = N(0.18, 2, 0x66);

                Gradients["BrushTitleBar"] = new[] { Solids["Brush2"], Solids["Brush3"], Solids["Brush2"] };
                Gradients["BrushBackground"] = new[] { N(0.972, 1.1), N(0.952, 1.7), N(0.928, 2.5) };
                Colors["ColorShadow"] = A(0.32, accent * 0.5);
                Solids["BrushShadowRing"] = A(0.32, accent * 0.5, 0x30);
            }

            private void FillDark(double hue, double accent, double neutral)
            {
                Color A(double l, double c, byte alpha = 255) => new Oklch(l, c, hue).ToColor(alpha);
                Color N(double l, double factor, byte alpha = 255) => new Oklch(l, neutral * factor, hue).ToColor(alpha);

                SetRamp(
                    N(0.93, 1.2),
                    A(0.86, accent * 0.75),
                    A(0.78, accent * 0.85),
                    A(0.70, accent * 0.8),
                    A(0.52, accent * 0.55),
                    A(0.37, Math.Min(0.06, accent * 0.4)),
                    A(0.32, Math.Min(0.05, accent * 0.33)),
                    A(0.285, Math.Min(0.04, accent * 0.27)));
                SetGrays(N(0.88, 0.6), N(0.76, 0.6), N(0.67, 0.6), N(0.55, 0.6), N(0.40, 0.6));

                Solids["BrushOnAccent"] = A(0.22, accent * 0.3);
                Solids["BrushTitleAccent"] = A(0.50, accent);
                Solids["BrushSurface"] = N(0.275, 1.2);
                Solids["BrushCard"] = N(0.255, 1.3, 0xE0);
                Solids["BrushCardHover"] = N(0.28, 1.3, 0xEB);
                Solids["BrushTile"] = N(0.28, 1.2, 0xB3);
                Solids["BrushInput"] = N(0.21, 1.2, 0xE6);
                Solids["BrushInputFocus"] = N(0.19, 1.2);
                Solids["BrushGreen"] = new Oklch(0.76, 0.14, 150).ToColor();
                Solids["BrushGreenLight"] = new Oklch(0.30, 0.045, 150).ToColor();
                Solids["BrushRed"] = new Oklch(0.71, 0.16, 25).ToColor();
                Solids["BrushRedLight"] = new Oklch(0.30, 0.055, 25).ToColor();
                Solids["BrushOrange"] = new Oklch(0.79, 0.14, 70).ToColor();
                Solids["BrushOrangeLight"] = new Oklch(0.31, 0.05, 70).ToColor();
                SetLampColors();
                Solids["BrushDialogMask"] = Color.FromArgb(0x8C, 0x05, 0x05, 0x08);

                Gradients["BrushTitleBar"] = new[] { A(0.43, accent * 0.85), A(0.51, accent * 0.9), A(0.43, accent * 0.85) };
                Gradients["BrushBackground"] = new[] { N(0.205, 1.5), N(0.185, 1.8), N(0.165, 2.0) };
                Colors["ColorShadow"] = Color.FromRgb(0, 0, 0);
                Solids["BrushShadowRing"] = Color.FromArgb(0x90, 0, 0, 0);
            }

            private void SetLampColors()
            {
                Solids["BrushLampGreen"] = Color.FromRgb(0x3C, 0xDC, 0x7C);
                Solids["BrushLampYellow"] = Color.FromRgb(0xFF, 0xD4, 0x3B);
                Solids["BrushLampRed"] = Color.FromRgb(0xFF, 0x4D, 0x4F);
            }

            private void SetRamp(params Color[] ramp)
            {
                for (int i = 0; i < ramp.Length; i++)
                {
                    Solids["Brush" + (i + 1)] = ramp[i];
                    Colors["Color" + (i + 1)] = ramp[i];
                }
            }

            private void SetGrays(Color gray1, Color gray3, Color gray4, Color gray5, Color gray7)
            {
                Solids["BrushGray1"] = gray1;
                Solids["BrushGray3"] = gray3;
                Solids["BrushGray4"] = gray4;
                Solids["BrushGray5"] = gray5;
                Solids["BrushGray7"] = gray7;
            }
        }
    }
}
