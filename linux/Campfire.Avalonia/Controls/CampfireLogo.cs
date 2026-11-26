// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：把原 Controls/CampfireLogo.cs 的「水平帧条 + 10fps 逐帧播放」移植到 Avalonia。
//       帧源换成用户提供的 Campfire_JE2_BE2.gif（300x300、40 帧、每帧 100ms）；
//       在 Windows 侧用纯标准库解码并合成为 Assets/CampfireFrames.png（3840x96 帧条，
//       每帧 96x96、透明背景），避免引入 Gif 解码用的新 NuGet 包。
//       原版用 WPF BitmapImage + CroppedBitmap + DispatcherTimer；这里换成
//       Avalonia AssetLoader + Bitmap + CroppedBitmap + DispatcherTimer，帧率与帧尺寸一致。

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Campfire.Avalonia.Controls
{
    /// <summary>标题栏左上角的动态营火徽标，帧条逐帧播放，10 fps。</summary>
    public sealed class CampfireLogo : Image
    {
        private const int FrameSize = 96;
        private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(100);
        private static CroppedBitmap[] _frames;

        private readonly DispatcherTimer _timer;
        private int _index;

        public CampfireLogo()
        {
            Stretch = Stretch.Uniform;
            // 像素画缩到 24x24 时保持硬边缘，别被双线性插值糊掉。
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
            Source = Frames[0];

            _timer = new DispatcherTimer { Interval = FrameTime };
            _timer.Tick += (_, __) =>
            {
                _index = (_index + 1) % Frames.Length;
                Source = Frames[_index];
            };
            // CAMPFIRE_ANIM=0 时整窗完全静止：徽标也停在第一帧，不另起一个 10fps timer。
            AttachedToVisualTree += (_, __) =>
            {
                if (UiAnimations.Enabled) _timer.Start();
            };
            DetachedFromVisualTree += (_, __) => _timer.Stop();
        }

        private static CroppedBitmap[] Frames
        {
            get
            {
                if (_frames != null)
                    return _frames;

                // avares:// 资源由 csproj 的 <AvaloniaResource Include="Assets/**" /> 嵌入。
                var uri = new Uri("avares://Campfire.Avalonia/Assets/CampfireFrames.png");
                var stream = AssetLoader.Open(uri);
                var strip = new Bitmap(stream);   // 帧条要一直被各 CroppedBitmap 引用，不能 Dispose
                int count = strip.PixelSize.Width / FrameSize;
                var frames = new CroppedBitmap[count];
                for (int i = 0; i < count; i++)
                    frames[i] = new CroppedBitmap(strip, new PixelRect(i * FrameSize, 0, FrameSize, FrameSize));
                _frames = frames;
                return frames;
            }
        }
    }
}
