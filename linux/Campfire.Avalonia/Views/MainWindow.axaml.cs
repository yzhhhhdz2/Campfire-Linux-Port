// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：Avalonia 版主窗口 code-behind，替代原 MainWindow.xaml.cs 的 DragMove / 最小化 / 最大化 / 关闭。
//       导航与会话状态都在 ViewModel，这里只处理窗口级操作与动效接线。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Campfire.Avalonia.Controls;

namespace Campfire.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();   // 由 Avalonia XAML 编译器生成，不要手写

            // 动效接线：窗口开场、涟漪层、状态点呼吸 + Ready 光环、营火微摇曳。
            // CAMPFIRE_ANIM=0 时 UiAnimations 内部一律不接管控件。
            UiAnimations.SetFxLayer(this.FindControl<Canvas>("FxLayer"));
            UiAnimations.AddStatusDot(this.FindControl<Ellipse>("StatusDot"),
                                      this.FindControl<Ellipse>("PulseRing"));
            UiAnimations.AddSway(this.FindControl<CampfireLogo>("Logo"));
            if (UiAnimations.Enabled)
                UiAnimations.AttachWindowIntro(this.FindControl<Grid>("PanRoot"));

            // 会话状态驱动标题栏状态点：连接中快脉冲，Ready 常亮并放一圈光环。
            var session = App.Session;
            if (session != null)
            {
                session.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == "Stage" || e.PropertyName == "IsActive")
                        UiAnimations.SetConnectionState(session.IsActive, session.Stage == "Ready");
                };
            }
        }

        private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void Minimize_Click(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void Close_Click(object sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            App.Session?.Shutdown();
            Close();
        }
    }
}
