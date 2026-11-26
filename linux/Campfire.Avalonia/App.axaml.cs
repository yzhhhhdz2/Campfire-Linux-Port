// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：Avalonia 版应用宿主，替代原 WPF 的 App.xaml.cs。
//       在 UI 线程创建唯一的 SessionService（持有 LinkSession），并挂到主窗口。

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Campfire.Avalonia.Bridge;
using Campfire.Avalonia.Localization;
using Campfire.Avalonia.Themes;
using Campfire.Avalonia.ViewModels;
using Campfire.Avalonia.Views;

namespace Campfire.Avalonia
{
    public partial class App : Application
    {
        /// <summary>长驻会话；页面导航时不会重建。</summary>
        public static SessionService Session { get; private set; }

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 语言要在任何 XAML 解析之前定好，否则首屏会闪一下旧语言。
                Loc.Load();
                // 先算好动态调色板，窗口创建时 {DynamicResource} 就能拿到目标色。
                ThemeManager.Initialize();
                Session = new SessionService(new AvaloniaUiContext());
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(Session)
                };
                desktop.Exit += (_, __) =>
                {
                    ThemeManager.Shutdown();
                    Session.Shutdown();
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
