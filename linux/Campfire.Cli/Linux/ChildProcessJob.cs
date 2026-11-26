// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：Windows Job Object（kernel32 CreateJobObject）在 Linux 无对等 API，
//       改为进程注册表 + 退出钩子兜底；正常路径由 EasyTierInstance.Dispose() 主动 Kill。

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// 语义对齐原版「Campfire 退出时连带结束 easytier 子进程」。
    /// Linux 版在进程退出 / Ctrl+C 时遍历已登记的子进程并结束它们。
    /// 注意：kill -9 或断电仍会留下残留进程，EasyTierInstance.Start 的 StopLeftovers 会再清一次。
    /// </summary>
    internal static class ChildProcessJob
    {
        private static readonly object Sync = new object();
        private static readonly List<Process> Children = new List<Process>();

        static ChildProcessJob()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => KillAll();
            Console.CancelKeyPress += (sender, e) =>
            {
                KillAll();
                e.Cancel = false;
            };
        }

        public static void Assign(Process process)
        {
            if (process == null)
                return;
            lock (Sync)
            {
                Children.RemoveAll(p => SafeHasExited(p));
                Children.Add(process);
            }
        }

        public static void Release(Process process)
        {
            if (process == null)
                return;
            lock (Sync)
                Children.Remove(process);
        }

        private static void KillAll()
        {
            List<Process> snapshot;
            lock (Sync)
            {
                snapshot = new List<Process>(Children);
                Children.Clear();
            }
            foreach (var process in snapshot)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception
                                           || ex is PlatformNotSupportedException)
                {
                }
            }
        }

        private static bool SafeHasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }
}
