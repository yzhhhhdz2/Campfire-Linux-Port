// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：换成 EasyTier 2.6.4 的 Linux 发行包（x86_64 / aarch64），去掉 .exe、Packet.dll、
//       Win7 门禁、IsWow64Process2 / AddDllDirectory 等 Win32 API，补 chmod +x。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Runtime
{
    /// <summary>
    /// 联机所依赖的 EasyTier 组件，首次使用时下载到数据目录。
    /// 版本必须与官方 Windows 客户端锁死在同一个 2.6.4，否则虚拟网络协议不兼容。
    /// </summary>
    internal static class EasyTierPackage
    {
        /// <summary>与 src/Campfire/Link/Runtime/EasyTierPackage.cs 完全一致，禁止改动。</summary>
        public const string Version = "2.6.4";

        // Linux 发行包里没有 .exe 后缀，也没有 Windows 专有的 Packet.dll。
        private const string CoreFile = "easytier-core";
        private const string CliFile = "easytier-cli";
        private static readonly string[] Files = { CoreFile, CliFile };
        private const string ChecksumFile = "checksums.sha256";

        private static readonly object Sync = new object();
        private static bool _verified;

        /// <summary>x86_64 / aarch64，对应 Release 资产名里的架构段。</summary>
        public static string Architecture { get; } = DetectArchitecture();

        // 下面三个属性只为兼容原工程的调用点保留；Linux 上语义固定。
        public static Version WindowsVersion => new Version(10, 0);
        public static bool IsSupportedSystem => true;
        public static bool IsWindows7 => false;   // 让 EasyTierConfig 走「启用 QUIC」的分支
        public static bool HasLoaderUpdate => true;

        public static string InstallDirectory =>
            Path.Combine(AppSettings.DataDirectory, "EasyTier", "linux-" + Version + "-" + Architecture);

        public static string CorePath => Path.Combine(InstallDirectory, CoreFile);

        public static string CliPath => Path.Combine(InstallDirectory, CliFile);

        /// <summary>已核对 GitHub Release v2.6.4 的 Linux 资产名。</summary>
        public static string AssetName => $"easytier-linux-{Architecture}-v{Version}.zip";

        public static IReadOnlyList<string> SourceUrls => GetSourceUrls().ToList();

        public static bool IsInstalled()
        {
            lock (Sync)
            {
                if (_verified)
                    return true;
                foreach (string file in Files)
                {
                    string path = Path.Combine(InstallDirectory, file);
                    if (!File.Exists(path))
                        return false;
                    if (OperatingSystem.IsWindows())
                        continue;
                    try
                    {
                        if ((File.GetUnixFileMode(path) & UnixFileMode.UserExecute) == 0)
                            return false;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                               || ex is PlatformNotSupportedException)
                    {
                        return false;
                    }
                }
                _verified = true;
                return true;
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                _verified = false;
                if (Directory.Exists(InstallDirectory))
                    Directory.Delete(InstallDirectory, true);
            }
        }

        /// <summary>
        /// 准备组件。优先使用环境变量 CAMPFIRE_EASYTIER_ZIP 指向的本地压缩包
        /// （学校网慢时可以先用下载工具断点续传下好再放进来），否则依次尝试国内镜像与 GitHub。
        /// </summary>
        public static async Task InstallAsync(IProgress<DownloadProgress> progress, CancellationToken cancellation)
        {
            if (IsInstalled())
                return;
            Directory.CreateDirectory(InstallDirectory);

            string localZip = Environment.GetEnvironmentVariable("CAMPFIRE_EASYTIER_ZIP");
            if (!string.IsNullOrWhiteSpace(localZip))
            {
                if (!File.Exists(localZip))
                    throw new LinkException("CAMPFIRE_EASYTIER_ZIP 指向的文件不存在：" + localZip);
                LinkLog.Info("Using local EasyTier archive: " + localZip);
                ExtractArchive(localZip);
                VerifyOrWarn();
                EnsureInstalled();
                return;
            }

            using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Campfire-Linux/" + LinuxConfig.AppVersion.ToString(3));
                Exception last = null;
                foreach (string url in GetSourceUrls())
                {
                    string temp = Path.Combine(InstallDirectory, "download.zip.tmp");
                    try
                    {
                        LinkLog.Info("Downloading EasyTier component from " + url);
                        await DownloadAsync(client, url, temp, progress, cancellation).ConfigureAwait(false);
                        ExtractArchive(temp);
                        TryDelete(temp);
                        VerifyOrWarn();
                        EnsureInstalled();
                        return;
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        last = ex;
                        TryDelete(temp);
                        LinkLog.Warn("Source " + url + " failed: " + ex.Message);
                    }
                }
                throw new LinkException(last, "Link.Error.DownloadFailed", last?.Message);
            }
        }

        private static void EnsureInstalled()
        {
            lock (Sync)
                _verified = false;
            if (!IsInstalled())
                throw new InvalidDataException("EasyTier 组件解压后仍不完整或没有可执行权限。");
        }

        // ======================== Sources ========================

        private static IEnumerable<string> GetSourceUrls()
        {
            // 已核对 GitHub Release v2.6.4：linux-x86_64 25.5 MB、linux-aarch64 20.5 MB。
            string github = $"https://github.com/EasyTier/EasyTier/releases/download/v{Version}/{AssetName}";
            yield return $"https://gitee.com/easytier/EasyTier/releases/download/v{Version}/{AssetName}";
            yield return github;
            yield return "https://ghfast.top/" + github;
            yield return "https://gh-proxy.com/" + github;
        }

        private static async Task DownloadAsync(HttpClient client, string url, string temp,
            IProgress<DownloadProgress> progress, CancellationToken cancellation)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                long received = 0;
                progress?.Report(new DownloadProgress(0, total));
                using (var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = File.Create(temp))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await network.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new DownloadProgress(received, total));
                    }
                }
            }
        }

        // ======================== Extract ========================

        private static void ExtractArchive(string archive)
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (string file in Files)
                {
                    var entry = zip.Entries.FirstOrDefault(e => MatchesEntry(e.FullName, file))
                        ?? throw new InvalidDataException("压缩包里找不到 " + file + "。");
                    string target = Path.Combine(InstallDirectory, file);
                    TryDelete(target);
                    entry.ExtractToFile(target, overwrite: true);
                    MakeExecutable(target);
                }
            }
        }

        private static bool MatchesEntry(string entryName, string file)
        {
            string name = entryName.Replace('\\', '/');
            return name.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, file, StringComparison.OrdinalIgnoreCase);
        }

        private static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
                return;
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is PlatformNotSupportedException)
            {
                LinkLog.Warn("无法给 " + path + " 设置可执行权限：" + ex.Message);
            }
        }

        // ======================== Verification ========================

        /// <summary>
        /// 官方 v2.6.4 Release 没有提供 Linux 资产的校验和文件，因此这里不编造哈希。
        /// 如果用户自己在 InstallDirectory 放了 checksums.sha256（每行 "&lt;hex&gt;  文件名"），
        /// 就逐项比对；否则记一条警告，提示手动用 sha256sum 核对。
        /// </summary>
        private static void VerifyOrWarn()
        {
            string list = Path.Combine(InstallDirectory, ChecksumFile);
            if (!File.Exists(list))
            {
                LinkLog.Warn("官方 v2.6.4 Release 未提供 Linux 校验和；跳过 SHA-256 校验，建议自行 sha256sum 核对。");
                return;
            }
            foreach (string line in File.ReadAllLines(list))
            {
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                string file = parts[parts.Length - 1].TrimStart('*');
                string path = Path.Combine(InstallDirectory, Path.GetFileName(file));
                if (!File.Exists(path))
                    continue;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                {
                    string actual = Hex.Of(sha.ComputeHash(stream));
                    if (!string.Equals(actual, parts[0], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(Path.GetFileName(path) + " 的 SHA-256 与 checksums.sha256 不符。");
                }
                LinkLog.Info("SHA-256 verified: " + Path.GetFileName(path));
            }
        }

        // ======================== Helpers ========================

        private static string DetectArchitecture()
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case System.Runtime.InteropServices.Architecture.X64:
                    return "x86_64";
                case System.Runtime.InteropServices.Architecture.Arm64:
                    return "aarch64";
                case System.Runtime.InteropServices.Architecture.Arm:
                    return "armv7";
                case System.Runtime.InteropServices.Architecture.X86:
                    return "i686";
                default:
                    return RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>启动一次 core --version，确认 Linux 能执行这个二进制。</summary>
        public static async Task<bool> CanRunAsync()
        {
            try
            {
                var start = new ProcessStartInfo(CorePath, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var process = Process.Start(start))
                {
                    var errors = process.StandardError.ReadToEndAsync();
                    bool exited = await Task.Run(() => process.WaitForExit(15000)).ConfigureAwait(false);
                    if (!exited)
                    {
                        process.Kill();
                        return true;
                    }
                    if (process.ExitCode != 0)
                        LinkLog.Warn("EasyTier self-test exited with " + process.ExitCode + ": " + (await errors.ConfigureAwait(false)).Trim());
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException
                                       || ex is IOException || ex is PlatformNotSupportedException)
            {
                LinkLog.Warn("EasyTier self-test failed: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>与官方 Windows 版同名的进度结构，供 InstallAsync 签名使用。</summary>
    internal readonly struct DownloadProgress
    {
        public DownloadProgress(long received, long total)
        {
            Received = received;
            Total = total;
        }

        public long Received { get; }

        public long Total { get; }

        public double Fraction => Total > 0 ? Math.Min(1.0, (double)Received / Total) : 0;
    }
}
