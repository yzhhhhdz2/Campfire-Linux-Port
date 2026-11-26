// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：改写自 linux/Campfire.Cli/Cli/SessionRuntime.cs:272-301 的 ApplyCliDefaults。
//       显式关掉阶段 1 已裁剪、但原版默认开启的功能，避免触发空实现或找不到游戏目录的分支。
//       GUI 不复用 CliHost.Drive（阻塞前台循环 + Console + session.json 轮询），只保留这段默认值逻辑。
//       2026-11-19：AutoOpenLan 改为「显式请求 + 未跳过世界探测」时才开启，
//       保持阶段 1 已验证的 --skip-probe 回归路径完全不变。

using System;
using Campfire.Link;

namespace Campfire.Avalonia.Bridge
{
    internal static class GuiDefaults
    {
        /// <summary>
        /// 建房页的「自动开放局域网世界」开关。只有用户显式勾选（或启动时带
        /// CAMPFIRE_AUTO_LAN=1）才为 true，避免没装 MC 的虚拟机卡在等待世界端口。
        /// </summary>
        public static bool AutoLanRequested { get; set; } =
            Environment.GetEnvironmentVariable("CAMPFIRE_AUTO_LAN") == "1";

        public static void Apply()
        {
            // VM 里没有真实 Minecraft 时，用这个开关跳过世界探测（与 CLI 的 --skip-probe 等价）。
            bool skip = Environment.GetEnvironmentVariable("CAMPFIRE_SKIP_PROBE") == "1";
            LinkSession.SkipProbe = skip;

            LinkSettings.AutoOpenLan = AutoLanRequested && !skip;
            LinkSettings.BackupWorld = false;      // 世界备份已裁剪
            LinkSettings.Voice = false;            // 语音已裁剪
            LinkSettings.ShareMods = false;        // 模组同步已裁剪
            LinkSettings.ServeMods = false;
            LinkSettings.AutoCopyCode = false;     // 由 GUI 的「复制」按钮显式触发
            LinkSettings.DetectClipboard = false;
        }
    }
}
