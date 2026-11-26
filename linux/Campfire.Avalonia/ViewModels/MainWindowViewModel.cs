// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增主窗口 VM。对应原 MainWindow.xaml.cs 的 Tab_Checked / ShowPage / OpenSubPage /
//       CloseSubPage：顶部 4 个胶囊 Tab 是根页，点磁贴把子页压栈，子页期间标题栏换成「返回 + 标题」。
//       换页前用 UiAnimations.NoteNavigation 告知方向（纯静态提示，不持有任何控件引用），
//       让新页面的入场从对应方向滑入。

using System;
using System.Collections.Generic;
using System.Windows.Input;
using Campfire.Avalonia.Controls;
using Campfire.Avalonia.Localization;

namespace Campfire.Avalonia.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        private readonly Stack<PageEntry> _stack = new Stack<PageEntry>();
        private readonly ViewModelBase _home;
        private readonly ViewModelBase _maps;
        private readonly ViewModelBase _tools;
        private readonly ViewModelBase _prefs;

        private ViewModelBase _current;
        private string _subTitleKey = "";
        private bool _homeChecked = true;
        private bool _mapsChecked;
        private bool _toolsChecked;
        private bool _settingsChecked;
        private int _lastTab;

        public SessionService Session { get; }

        public MainWindowViewModel(SessionService session)
        {
            Session = session;
            _home = new HomeViewModel(this);
            _maps = new MapsViewModel(this);
            _tools = new ToolsViewModel(this);
            _prefs = new PreferencesViewModel(this);
            _current = _home;
        }

        public ViewModelBase Current { get => _current; private set => Set(ref _current, value); }
        public bool InSubPage => _stack.Count > 0;
        public string SubTitle => string.IsNullOrEmpty(_subTitleKey) ? "" : Loc.Get(_subTitleKey);

        /// <summary>标题栏语言按钮的短码与提示；语言切换时 RaiseAll 会一并刷新。</summary>
        public string LanguageLabel => Loc.Label;
        public string LanguageTooltip => Loc.Tooltip;

        public bool HomeChecked { get => _homeChecked; set => Set(ref _homeChecked, value); }
        public bool MapsChecked { get => _mapsChecked; set => Set(ref _mapsChecked, value); }
        public bool ToolsChecked { get => _toolsChecked; set => Set(ref _toolsChecked, value); }
        public bool SettingsChecked { get => _settingsChecked; set => Set(ref _settingsChecked, value); }

        private ICommand Lazy(ref ICommand field, Action action)
            => field ?? (field = new RelayCommand(action));

        private ICommand _showHome, _showMaps, _showTools, _showSettings, _back, _cycleLanguage;

        public ICommand ShowHome => Lazy(ref _showHome, () => ShowTab(0));
        public ICommand ShowMaps => Lazy(ref _showMaps, () => ShowTab(1));
        public ICommand ShowTools => Lazy(ref _showTools, () => ShowTab(2));
        public ICommand ShowSettings => Lazy(ref _showSettings, () => ShowTab(3));
        public ICommand Back => Lazy(ref _back, () => CloseSubPage());
        public ICommand CycleLanguage => Lazy(ref _cycleLanguage, () =>
        {
            Loc.SetLanguage(Loc.NextLanguage());
            Raise(nameof(LanguageLabel));
            Raise(nameof(LanguageTooltip));
            Raise(nameof(SubTitle));
        });

        private void ShowTab(int index)
        {
            if (InSubPage)
                return;
            _stack.Clear();
            UiAnimations.NoteNavigation(index > _lastTab
                ? NavDirection.Right : NavDirection.Left);
            _lastTab = index;
            ViewModelBase page;
            switch (index)
            {
                case 0: page = _home; break;
                case 1: page = _maps; break;
                case 2: page = _tools; break;
                default: page = _prefs; break;
            }
            Current = page;
            HomeChecked = index == 0;
            MapsChecked = index == 1;
            ToolsChecked = index == 2;
            SettingsChecked = index == 3;
        }

        /// <summary>titleKey 存本地化键而非译文，切换语言后标题栏会跟着变。</summary>
        public void OpenSubPage(ViewModelBase page, string titleKey)
        {
            UiAnimations.NoteNavigation(NavDirection.Into);
            _stack.Push(new PageEntry(Current, _subTitleKey));
            Current = page;
            _subTitleKey = titleKey;
            Raise(nameof(SubTitle));
            Raise(nameof(InSubPage));
        }

        private void CloseSubPage()
        {
            if (_stack.Count == 0)
                return;
            UiAnimations.NoteNavigation(NavDirection.Back);
            PageEntry entry = _stack.Pop();
            Current = entry.Page;
            _subTitleKey = entry.Title;
            Raise(nameof(SubTitle));
            Raise(nameof(InSubPage));
        }

        private readonly struct PageEntry
        {
            public PageEntry(ViewModelBase page, string title)
            {
                Page = page;
                Title = title;
            }
            public ViewModelBase Page { get; }
            public string Title { get; }
        }
    }

    internal sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;

        public RelayCommand(Action execute) : this(_ => execute()) { }
        public RelayCommand(Action<object> execute) { _execute = execute; }

        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
    }
}
