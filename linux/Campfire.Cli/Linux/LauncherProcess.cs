// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：以 /proc/<pid>/exe 符号链接替代 Windows 的 kernel32 QueryFullProcessImageName。

using System;
using System.Diagnostics;
using System.IO;

namespace Campfire.Launcher
{
    /// <summary>
    /// EasyTierInstance.StopLeftovers 用它判断残留的 easytier 进程是不是本程序装的。
    /// Linux 下读 /proc/&lt;pid&gt;/exe 的真实路径；拿不到时退回 argv[0]。
    /// </summary>
    internal static class LauncherProcess
    {
        internal static string TryGetImagePath(Process process)
        {
            if (process == null)
                return null;
            try
            {
                string exe = "/proc/" + process.Id + "/exe";
                if (File.Exists(exe))
                {
                    var target = File.ResolveLinkTarget(exe, returnFinalTarget: true);
                    if (target != null && target.FullName.Length > 0)
                        return target.FullName;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is InvalidOperationException || ex is PlatformNotSupportedException)
            {
                // 权限不足或不是 Linux：继续用命令行回退。
            }

            try
            {
                string cmdline = File.ReadAllText("/proc/" + process.Id + "/cmdline");
                int end = cmdline.IndexOf('\0');
                string argv0 = end > 0 ? cmdline.Substring(0, end) : cmdline;
                return argv0.Length > 0 ? argv0 : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
