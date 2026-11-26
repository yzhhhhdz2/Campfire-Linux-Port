// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：用 Avalonia 的 UI 线程调度器替代原 WPF 的 DispatcherSynchronizationContext。
//       LinkSession 会把后台线程的 Changed 事件 Post 到这个上下文，天然适配 GUI。

using System.Threading;
using Avalonia.Threading;

namespace Campfire.Avalonia.Bridge
{
    internal sealed class AvaloniaUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object state)
        {
            if (callback == null)
                return;
            Dispatcher.UIThread.Post(() => callback(state), DispatcherPriority.Normal);
        }

        public override void Send(SendOrPostCallback callback, object state)
        {
            if (callback == null)
                return;
            Dispatcher.UIThread.InvokeAsync(() => callback(state)).GetAwaiter().GetResult();
        }
    }
}
