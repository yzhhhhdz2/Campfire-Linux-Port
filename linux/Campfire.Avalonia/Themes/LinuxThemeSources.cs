// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：替代 src/Campfire/Themes/WallpaperColors.cs 里的 Win32 / 注册表调用。
//       Score()（按色相分箱 + 覆盖率/彩度评分 + 色相间距挑选）与 SampleSize/MaxColors 逐字移植；
//       LoadWallpaper/SystemParametersInfo/DwmGetColorizationColor/Registry 全部换成
//       gsettings（GNOME）+ SkiaSharp 解码。差异见 linux/README.md「动态主题」章节。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Media;
using SkiaSharp;

namespace Campfire.Avalonia.Themes
{
    /// <summary>
    /// Linux 侧的系统主题信息源。原版读注册表 AppsUseLightTheme、DWM 强调色、Win32 壁纸路径；
    /// 这里改用 GNOME gsettings，取不到时逐级降级，绝不因缺命令而中断主题。
    /// </summary>
    internal static class LinuxThemeSources
    {
        private const int SampleSize = 128;
        private const int MaxColors = 4;

        /// <summary>GNOME 47 的 9 个强调色（accent-color 枚举对应的实际色值）。</summary>
        private static readonly Dictionary<string, Color> GnomeAccents = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = Color.FromRgb(0x35, 0x84, 0xE4),
            ["teal"] = Color.FromRgb(0x21, 0x90, 0xA4),
            ["green"] = Color.FromRgb(0x3A, 0x91, 0x45),
            ["yellow"] = Color.FromRgb(0xC8, 0x88, 0x00),
            ["orange"] = Color.FromRgb(0xED, 0x5B, 0x00),
            ["red"] = Color.FromRgb(0xE6, 0x2D, 0x42),
            ["pink"] = Color.FromRgb(0xD5, 0x61, 0x99),
            ["purple"] = Color.FromRgb(0x91, 0x41, 0xAC),
            ["slate"] = Color.FromRgb(0x6F, 0x83, 0x96),
        };

        /// <summary>系统是否偏好深色；GNOME color-scheme 优先，其次 gtk-theme / GTK_THEME 环境变量。</summary>
        public static bool SystemPrefersDark()
        {
            string scheme = GSettings("get", "org.gnome.desktop.interface", "color-scheme");
            if (!string.IsNullOrEmpty(scheme))
                return scheme.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;

            string gtk = GSettings("get", "org.gnome.desktop.interface", "gtk-theme");
            if (gtk != null && gtk.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string env = Environment.GetEnvironmentVariable("GTK_THEME") ?? "";
            return env.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>GNOME 强调色；老版本 GNOME 没有 accent-color 键时返回 null，由调用方回退默认篝火橙。</summary>
        public static Color? SystemAccent()
        {
            string raw = GSettings("get", "org.gnome.desktop.interface", "accent-color");
            string name = Unquote(raw);
            if (name != null && GnomeAccents.TryGetValue(name, out Color color))
                return color;
            return null;
        }

        /// <summary>从桌面壁纸里挑最多 4 个候选色；取不到时退回纯色桌面或空列表。</summary>
        public static IReadOnlyList<Color> Extract()
        {
            try
            {
                foreach (string path in WallpaperPaths())
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                        continue;
                    List<Color> pixels = Sample(path);
                    if (pixels.Count == 0)
                        continue;
                    IReadOnlyList<Color> colors = Score(pixels);
                    if (colors.Count > 0)
                        return colors;
                }

                string solid = Unquote(GSettings("get", "org.gnome.desktop.background", "primary-color"));
                if (solid != null)
                {
                    Color parsed = ColorMath.Parse(solid, Colors.Transparent);
                    if (parsed.A == 255 && Oklch.FromColor(parsed).C >= 0.04)
                        return new[] { parsed };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is OutOfMemoryException)
            {
                // 壁纸缺失 / 格式不支持 / 权限不足都只影响取色，不影响主题本身。
            }
            return Array.Empty<Color>();
        }

        /// <summary>壁纸文件路径候选：GNOME 深色/浅色 picture-uri，其次常见用户目录。</summary>
        private static IEnumerable<string> WallpaperPaths()
        {
            foreach (string key in new[] { "picture-uri-dark", "picture-uri" })
            {
                string raw = Unquote(GSettings("get", "org.gnome.desktop.background", key));
                if (string.IsNullOrEmpty(raw))
                    continue;

                // 迭代器方法里 yield return 不能放进带 catch 的 try，所以先算出路径再 yield。
                string path = null;
                if (raw.StartsWith("file://", StringComparison.Ordinal))
                {
                    try { path = Uri.UnescapeDataString(new Uri(raw).LocalPath); }
                    catch (UriFormatException) { }
                }
                else if (Path.IsPathRooted(raw))
                {
                    path = raw;
                }
                if (path != null)
                    yield return path;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, "Pictures", "wallpaper");
                yield return Path.Combine(home, ".config", "wallpaper");
            }
        }

        /// <summary>用 SkiaSharp 解码并缩到 128x128，逐像素读出不透明颜色（对应原版 Sample）。</summary>
        private static List<Color> Sample(string path)
        {
            var colors = new List<Color>(SampleSize * SampleSize);
            using (SKBitmap original = SKBitmap.Decode(path))
            {
                if (original == null)
                    return colors;
                var info = new SKImageInfo(SampleSize, SampleSize, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (SKBitmap small = original.Resize(info, SKFilterQuality.Medium))
                {
                    if (small == null)
                        return colors;
                    for (int y = 0; y < SampleSize; y++)
                        for (int x = 0; x < SampleSize; x++)
                        {
                            SKColor pixel = small.GetPixel(x, y);
                            if (pixel.Alpha == 255)
                                colors.Add(Color.FromRgb(pixel.Red, pixel.Green, pixel.Blue));
                        }
                }
            }
            return colors;
        }

        /// <summary>
        /// Groups pixels by hue, then rates each hue by how much of the picture lies near it and how colorful
        /// it is. Gray, very dark and very bright pixels never decide the theme. 逐字移植原 WallpaperColors.Score。
        /// </summary>
        private static IReadOnlyList<Color> Score(List<Color> pixels)
        {
            var counts = new double[360];
            var sumL = new double[360];
            var sumC = new double[360];
            int usable = 0;
            foreach (var pixel in pixels)
            {
                var color = Oklch.FromColor(pixel);
                if (color.C < 0.03 || color.L < 0.2 || color.L > 0.96)
                    continue;
                int bin = (int)color.H % 360;
                counts[bin]++;
                sumL[bin] += color.L;
                sumC[bin] += color.C;
                usable++;
            }
            if (usable < pixels.Count * 0.01 || usable == 0)
                return Array.Empty<Color>();

            var candidates = new List<(double Score, Oklch Color)>();
            for (int hue = 0; hue < 360; hue++)
            {
                if (counts[hue] == 0)
                    continue;

                double near = 0;
                for (int offset = -15; offset <= 15; offset++)
                    near += counts[(hue + offset + 360) % 360];
                double share = near / usable;
                double chroma = sumC[hue] / counts[hue];
                if (share < 0.01 || chroma < 0.04)
                    continue;

                double lightness = sumL[hue] / counts[hue];
                double score = share * 70 + (chroma - 0.12) * (chroma < 0.12 ? 25 : 75);
                candidates.Add((score, new Oklch(lightness, chroma, hue)));
            }

            var sorted = candidates.OrderByDescending(c => c.Score).ToList();
            for (int spacing = 90; spacing >= 15; spacing -= 15)
            {
                var chosen = new List<Oklch>();
                foreach (var candidate in sorted)
                {
                    if (chosen.All(c => HueDistance(c.H, candidate.Color.H) >= spacing))
                        chosen.Add(candidate.Color);
                    if (chosen.Count == MaxColors)
                        break;
                }
                if (chosen.Count == MaxColors || spacing == 15)
                    return chosen.Select(c => c.ToColor()).ToList();
            }
            return Array.Empty<Color>();
        }

        private static double HueDistance(double first, double second)
        {
            double distance = Math.Abs(first - second) % 360;
            return distance > 180 ? 360 - distance : distance;
        }

        /// <summary>同步跑一次 gsettings，2 秒超时；命令不存在或报错返回 null。</summary>
        private static string GSettings(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("gsettings")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                foreach (string arg in args)
                    psi.ArgumentList.Add(arg);

                using (Process process = Process.Start(psi))
                {
                    if (process == null)
                        return null;
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(2000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return null;
                    }
                    return process.ExitCode == 0 ? output.Trim() : null;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return null;
            }
        }

        private static string Unquote(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return raw.Trim().Trim('\'', '"').Trim();
        }
    }
}
