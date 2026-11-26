// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：7 个页面 View 的 code-behind。原 WPF 每个 Page 都有大量 code-behind；
//       Avalonia 版状态与命令全部放在 ViewModel，这里只做 XAML 加载与动效接线。
//       多个 partial 类放在同一文件，XAML 编译器会为每个 x:Class 生成 InitializeComponent。

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Campfire.Avalonia.Controls;
using Campfire.Avalonia.ViewModels;

namespace Campfire.Avalonia.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView() { InitializeComponent(); UiAnimations.AttachEnter(this); }
    }

    public partial class MapsView : UserControl
    {
        public MapsView() { InitializeComponent(); UiAnimations.AttachEnter(this); }
    }

    public partial class WorldView : UserControl
    {
        public WorldView() { InitializeComponent(); UiAnimations.AttachEnter(this); }
    }

    public partial class ToolsView : UserControl
    {
        public ToolsView() { InitializeComponent(); UiAnimations.AttachEnter(this); }
    }

    public partial class PreferencesView : UserControl
    {
        public PreferencesView() { InitializeComponent(); UiAnimations.AttachEnter(this); }
    }

    public partial class LinkView : UserControl
    {
        private bool _typewriterWired;

        public LinkView()
        {
            InitializeComponent();
            UiAnimations.AttachEnter(this);

            // 邀请码不再用 Binding：由打字机逐字写入，避免绑定值与中间态互相覆盖。
            // 关掉动画时 AttachTypewriter 仍会订阅并直接显示全文，内容不会丢。
            var codeText = this.FindControl<TextBlock>("CodeText");
            DataContextChanged += (_, __) =>
            {
                if (_typewriterWired || !(DataContext is LinkViewModel vm)) return;
                _typewriterWired = true;
                UiAnimations.AttachTypewriter(codeText, vm.Session, "Code", () => vm.Session.Code);
            };
        }
    }

    public partial class LobbyView : UserControl
    {
        public LobbyView() { InitializeComponent(); UiAnimations.AttachEnter(this); }
    }
}
