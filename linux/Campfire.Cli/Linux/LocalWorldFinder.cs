// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：以 /proc/net/tcp + /proc/<pid>/fd 替代 iphlpapi.dll GetExtendedTcpTable。
//       2026-11-19 补上 FindListeningPort()：Java Agent 自动开放世界后会新出现一个
//       LISTEN 端口，WaitForLocalWorldAsync 靠它把端口交给 LinkSession 继续建房。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// 找出监听「对局域网开放」端口的 Java 进程。
    /// Linux 做法：从 /proc/net/tcp(6) 找 LISTEN 且本地端口匹配的 socket inode，
    /// 再遍历 /proc/*/fd/* 的符号链接找 socket:[inode] 归属哪个 pid。
    /// 不需要 root（读自己的进程）；读别人的 fd 可能被权限拒绝，会安静跳过。
    /// </summary>
    internal static class LocalWorldFinder
    {
        /// <summary>轮询等待游戏开放局域网端口；也可由 Java Agent 自动开放。</summary>
        public static async Task<int> WaitForLocalWorldAsync(CancellationToken cancellation, Func<int, bool> accept = null)
        {
            while (!cancellation.IsCancellationRequested)
            {
                int port = FindListeningPort();
                if (port > 0 && (accept == null || accept(port)))
                    return port;
                try
                {
                    await Task.Delay(500, cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            return 0;
        }

        public static int FindProcess(int port)
        {
            if (!OperatingSystem.IsLinux() || port <= 0)
                return 0;

            var inodes = new HashSet<long>();
            CollectInodes("/proc/net/tcp", port, inodes);
            CollectInodes("/proc/net/tcp6", port, inodes);
            if (inodes.Count == 0)
                return 0;

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (long inode in inodes)
                wanted.Add("socket:[" + inode.ToString(CultureInfo.InvariantCulture) + "]");

            try
            {
                foreach (string directory in Directory.EnumerateDirectories("/proc"))
                {
                    string name = Path.GetFileName(directory);
                    if (name.Length == 0 || !char.IsDigit(name[0]) || !int.TryParse(name, out int pid))
                        continue;
                    string fdDirectory = Path.Combine(directory, "fd");
                    try
                    {
                        foreach (string fd in Directory.EnumerateFileSystemEntries(fdDirectory))
                        {
                            try
                            {
                                string target = new FileInfo(fd).LinkTarget;
                                if (target != null && wanted.Contains(target))
                                    return pid;
                            }
                            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                                       || ex is InvalidOperationException || ex is PlatformNotSupportedException)
                            {
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return 0;
        }

        public static bool HasUdpSocket(int port, int pid)
        {
            if (!OperatingSystem.IsLinux() || port <= 0 || pid <= 0)
                return false;
            var inodes = new HashSet<long>();
            CollectInodes("/proc/net/udp", port, inodes);
            CollectInodes("/proc/net/udp6", port, inodes);
            if (inodes.Count == 0)
                return false;
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (long inode in inodes)
                wanted.Add("socket:[" + inode.ToString(CultureInfo.InvariantCulture) + "]");
            try
            {
                foreach (string fd in Directory.EnumerateFileSystemEntries("/proc/" + pid + "/fd"))
                {
                    string target = new FileInfo(fd).LinkTarget;
                    if (target != null && wanted.Contains(target))
                        return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is InvalidOperationException || ex is PlatformNotSupportedException)
            {
            }
            return false;
        }

        /// <summary>扫描 /proc/net/* 表：第 2 列 local_address=HEXIP:HEXPORT，第 4 列 st=0A 表示 LISTEN，最后一列是 inode。</summary>
        private static void CollectInodes(string table, int port, HashSet<long> result)
        {
            try
            {
                foreach (string line in File.ReadAllLines(table))
                {
                    string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (columns.Length < 10)
                        continue;
                    int colon = columns[1].LastIndexOf(':');
                    if (colon < 0 || !int.TryParse(columns[1].Substring(colon + 1), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out int localPort) || localPort != port)
                        continue;
                    // UDP 没有 LISTEN 状态；UDP 表不做状态过滤。
                    if (!table.Contains("udp") && !columns[3].Equals("0A", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (long.TryParse(columns[9], NumberStyles.None, CultureInfo.InvariantCulture, out long inode) && inode > 0)
                        result.Add(inode);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// 找 Minecraft 客户端刚开放出来的局域网端口。先收集所有 TCP LISTEN 的 inode→端口，
        /// 再遍历 /proc/*/fd，只认属于 MC java 进程的那个 socket。
        /// </summary>
        private static int FindListeningPort()
        {
            if (!OperatingSystem.IsLinux())
                return 0;

            var portByInode = new Dictionary<long, int>();
            CollectListenPorts("/proc/net/tcp", portByInode);
            CollectListenPorts("/proc/net/tcp6", portByInode);
            if (portByInode.Count == 0)
                return 0;

            var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in portByInode)
                wanted["socket:[" + pair.Key.ToString(CultureInfo.InvariantCulture) + "]"] = pair.Value;

            try
            {
                foreach (string directory in Directory.EnumerateDirectories("/proc"))
                {
                    string name = Path.GetFileName(directory);
                    if (name.Length == 0 || !char.IsDigit(name[0])
                        || !int.TryParse(name, out int pid) || pid <= 0)
                        continue;
                    // 只认 Minecraft 客户端，别把别的 java 服务端口当成游戏端口。
                    if (Campfire.Worlds.RunningGame.FromProcess(pid) == null)
                        continue;
                    try
                    {
                        foreach (string fd in Directory.EnumerateFileSystemEntries(Path.Combine(directory, "fd")))
                        {
                            string target;
                            try { target = new FileInfo(fd).LinkTarget; }
                            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                                       || ex is InvalidOperationException || ex is PlatformNotSupportedException)
                            { continue; }
                            if (target != null && wanted.TryGetValue(target, out int port))
                                return port;
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return 0;
        }

        private static void CollectListenPorts(string table, Dictionary<long, int> result)
        {
            try
            {
                foreach (string line in File.ReadAllLines(table))
                {
                    string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (columns.Length < 10)
                        continue;
                    if (!columns[3].Equals("0A", StringComparison.OrdinalIgnoreCase))
                        continue;
                    int colon = columns[1].LastIndexOf(':');
                    if (colon < 0 || !int.TryParse(columns[1].Substring(colon + 1), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out int port) || port <= 0)
                        continue;
                    if (long.TryParse(columns[9], NumberStyles.None, CultureInfo.InvariantCulture, out long inode) && inode > 0)
                        result[inode] = port;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
