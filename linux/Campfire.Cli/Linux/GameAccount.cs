// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：以 /proc/<pid>/cmdline 替代 WMI Win32_Process + OpenProcess + NtQueryInformationProcess。

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// 从运行中的 Minecraft（Java）进程读取玩家名与 UUID。
    /// Linux 的 /proc/&lt;pid&gt;/cmdline 本身就是 NUL 分隔的 argv，比 Windows 版读 PEB 命令行简单得多。
    /// 读不到时 Current 为 null，LinkSettings 会退回用户用 --player-name 指定的名字。
    /// </summary>
    internal sealed class GameAccount
    {
        private GameAccount(string name, string uuid, bool isPremium, int processId)
        {
            Name = name;
            Uuid = uuid;
            IsPremium = isPremium;
            ProcessId = processId;
        }

        public string Name { get; }

        public string Uuid { get; }

        public bool IsPremium { get; }

        public int ProcessId { get; }

        public static GameAccount Current { get; private set; }

        public static Task<GameAccount> RefreshAsync()
        {
            Current = Find();
            return Task.FromResult(Current);
        }

        /// <summary>/proc cmdline 的 NUL 分隔 argv；同时兼容传入普通命令行字符串。</summary>
        internal static List<string> Split(string commandLine)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(commandLine))
                return parts;
            foreach (string part in commandLine.Split(new[] { '\0', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                parts.Add(part.Trim());
            return parts;
        }

        internal static string ReadCommandLine(int processId)
        {
            try
            {
                return File.ReadAllText("/proc/" + processId + "/cmdline");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return "";
            }
        }

        private static GameAccount Find()
        {
            if (!OperatingSystem.IsLinux())
                return null;
            try
            {
                foreach (string directory in Directory.EnumerateDirectories("/proc"))
                {
                    string name = Path.GetFileName(directory);
                    if (name.Length == 0 || !char.IsDigit(name[0]) || !int.TryParse(name, out int pid))
                        continue;

                    string raw = ReadCommandLine(pid);
                    if (raw.IndexOf("--username", StringComparison.Ordinal) < 0)
                        continue;

                    var args = Split(raw);
                    string user = Option(args, "--username");
                    if (string.IsNullOrEmpty(user))
                        continue;
                    string uuid = Option(args, "--uuid") ?? "";
                    string type = Option(args, "--userType") ?? "";
                    return new GameAccount(user, uuid,
                        type.Equals("msa", StringComparison.OrdinalIgnoreCase)
                        || type.Equals("mojang", StringComparison.OrdinalIgnoreCase), pid);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return null;
        }

        private static string Option(IReadOnlyList<string> args, string key)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.Ordinal))
                    return args[i + 1];
            }
            return null;
        }
    }
}
