// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增 ViewModel → View 的约定映射（XxxViewModel → Views/XxxView），
//       对应原 WPF 由 MainWindow 手动切换 Visibility 的页面栈。

using Campfire.Avalonia.Localization;
using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Campfire.Avalonia.ViewModels;

namespace Campfire.Avalonia
{
    public sealed class ViewLocator : IDataTemplate
    {
        public Control Build(object data)
        {
            string full = data.GetType().FullName ?? "";
            string viewName = full.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
            Type type = Type.GetType(viewName);
            if (type != null)
                return (Control)Activator.CreateInstance(type);
            return new TextBlock { Text = Loc.Get("ViewNotFound") + viewName };
        }

        public bool Match(object data) => data is ViewModelBase;
    }
}
