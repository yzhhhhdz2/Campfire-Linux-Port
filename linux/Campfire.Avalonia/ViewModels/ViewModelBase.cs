// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增最小的 MVVM 基类（不引入 ReactiveUI，压到最小离线包）。
//       第二批补三语本地化：暴露 L 索引器供 XAML 绑定，语言变化时 RaiseAll()
//       让带参数的动态文案（房间状态、诊断信息等）重新取值。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Campfire.Avalonia.Localization;

namespace Campfire.Avalonia.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        // 弱引用订阅的前提：调用方自己持有委托的强引用，VM 被回收后自然失效。
        private readonly Action _onLanguageChanged;

        protected ViewModelBase()
        {
            _onLanguageChanged = RaiseAll;
            Loc.Subscribe(_onLanguageChanged);
        }

        /// <summary>XAML 里写 {Binding L[Key]}；语言切换时索引器通知会刷新全部静态文案。</summary>
        public LocSource L => LocSource.Instance;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>propertyName = null 表示「所有属性都变了」，绑定会重新读取 getter。</summary>
        protected void RaiseAll()
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            Raise(name);
            return true;
        }
    }
}
