// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增 Avalonia 11 桌面入口，替代原 WPF 的 App.xaml.cs / MainWindow 宿主。
//       原 Windows 工程全程零改动；联机协议层通过共享源码链接复用。

using System;
using Avalonia;

namespace Campfire.Avalonia
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
