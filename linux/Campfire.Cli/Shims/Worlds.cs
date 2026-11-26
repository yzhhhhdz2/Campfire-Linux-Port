// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：原版靠 WMI / Windows 进程命令行枚举正在运行的 Minecraft；这里改成读 /proc，
//       供 OpenWorldToLanAsync → Worlds.RunningGame.Find() 拿到游戏 PID 后注入 Java Agent。
//       世界备份 / 版本识别仍按阶段 1 约定保持裁剪（FindVersion 返回 null、BackupWorld=false）。

using System;
using System.Collections.Generic;
using System.IO;

namespace Campfire.Worlds
{
    internal enum VersionLoader
    {
        Vanilla,
        Forge,
        NeoForge,
        Fabric,
        Quilt,
    }

    internal sealed class InstalledVersion
    {
        public string GameVersion => "";
        public VersionLoader Loader => VersionLoader.Vanilla;
    }

    /// <summary>
    /// 一个正在运行的 Minecraft 客户端。判定条件：/proc/&lt;pid&gt;/exe 指向 java*，
    /// 且命令行含 MC 主类或启动器的 --gameDir/--workDir + minecraft。
    /// 读别人的 /proc 可能被权限拒绝，安静跳过。
    /// </summary>
    internal sealed class RunningGame
    {
        public int ProcessId { get; private set; }
        public string GameDirectory { get; private set; } = "";
        public string SavesDirectory => string.IsNullOrEmpty(GameDirectory)
            ? "" : Path.Combine(GameDirectory, "saves");

        /// <summary>版本识别仍属裁剪范围；Java Agent 用官方名反射，不依赖加载器版本。</summary>
        public InstalledVersion FindVersion() => null;

        public static RunningGame FromProcess(int processId)
        {
            return processId > 0 && OperatingSystem.IsLinux() ? Parse(processId) : null;
        }

        public static IReadOnlyList<RunningGame> Find()
        {
            var games = new List<RunningGame>();
            if (!OperatingSystem.IsLinux())
                return games;
            try
            {
                foreach (string directory in Directory.EnumerateDirectories("/proc"))
                {
                    string name = Path.GetFileName(directory);
                    if (name.Length == 0 || !char.IsDigit(name[0])
                        || !int.TryParse(name, out int pid) || pid <= 0)
                        continue;
                    RunningGame game = Parse(pid);
                    if (game != null)
                        games.Add(game);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return games;
        }

        private static RunningGame Parse(int pid)
        {
            string cmdline;
            try
            {
                string exe = new FileInfo("/proc/" + pid + "/exe").LinkTarget;
                if (string.IsNullOrEmpty(exe)
                    || !Path.GetFileName(exe).StartsWith("java", StringComparison.OrdinalIgnoreCase))
                    return null;
                cmdline = File.ReadAllText("/proc/" + pid + "/cmdline").Replace('\0', ' ');
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (PlatformNotSupportedException) { return null; }

            if (!LooksLikeMinecraft(cmdline))
                return null;

            return new RunningGame
            {
                ProcessId = pid,
                GameDirectory = GameDirectoryFrom(cmdline),
            };
        }

        private static bool LooksLikeMinecraft(string cmdline)
        {
            // 官方启动器 / Forge / NeoForge / Fabric / 旧 Forge 的主类
            string[] mainClasses =
            {
                "net.minecraft.client.main.Main",
                "net.minecraft.launchwrapper.Launch",
                "cpw.mods.bootstraplauncher.BootstrapLauncher",
                "net.fabricmc.loader.launch.knot.KnotClient",
                "net.fabricmc.loader.impl.launch.knot.KnotClient",
                "org.quiltmc.loader.impl.launch.knot.KnotClient",
            };
            foreach (string main in mainClasses)
                if (cmdline.IndexOf(main, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            // Prism / MultiMC 等：命令行里带 --gameDir 且路径落在 minecraft 目录
            bool hasGameDir = cmdline.IndexOf("--gameDir", StringComparison.OrdinalIgnoreCase) >= 0
                           || cmdline.IndexOf("--workDir", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasGameDir && cmdline.IndexOf("minecraft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GameDirectoryFrom(string cmdline)
        {
            string[] parts = cmdline.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], "--gameDir", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parts[i], "--workDir", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            }
            return "";
        }
    }

    internal sealed class WorldSource
    {
        public IReadOnlyList<WorldInfo> Worlds => Array.Empty<WorldInfo>();
        public static WorldSource Open(string path) => null;
    }

    internal sealed class WorldInfo
    {
    }

    internal static class WorldImporter
    {
        public static string Import(WorldSource source, WorldInfo world, string savesDirectory) => "";
    }
}
