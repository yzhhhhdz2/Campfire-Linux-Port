// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：把原 Link/Minecraft/GameMappings.cs 移植到 Linux。原文件是纯托管代码，仅
//       FindVersionFile() 里的 classpath 分隔符与 "\\libraries\\" 是 Windows 路径写法；
//       这里改成 Linux 的 ':' 与 "/libraries/"，其余逻辑（版本 json → client_mappings
//       的 URL/SHA-1、按内容哈希缓存、内嵌老版本名字表、Mojang/镜像双源）逐字保留。
//       另外把下载顺序改成国内镜像优先，并把单源超时从 90s 降到 30s，
//       避免校园网访问 piston-data.mojang.com 卡住整个建房流程。
//
// 为什么需要：1.14.4–1.21.11 的官方启动器正式版都是混淆版，游戏 jar 里
// publishServer / tickServer 等名字被换成了 a / b / abc；Java Agent 靠 Mojang 官方
// client_mappings（ProGuard 格式）把官方名还原成运行时名，才能反射调用。
// 26.1 起不再混淆，或以 Mojmap 官方名运行的加载器（Fabric/Forge）不需要映射表。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// Provides the official mapping file of an obfuscated release, which the in-game agent needs to find
    /// Minecraft's classes up to 1.21.11. Releases since 26.1 are not obfuscated and need none.
    /// </summary>
    internal static class GameMappings
    {
        private static readonly Lazy<HttpClient> Client =
            new Lazy<HttpClient>(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private const string MojangHost = "https://piston-data.mojang.com/";
        private const string MirrorHost = "https://bmclapi2.bangbang93.com/";

        private static string Directory => Path.Combine(AppSettings.DataDirectory, "mappings");

        private const string EmbeddedPrefix = "embedded:";
        private const string LegacyPrefix = "Campfire.Mappings.";

        /// <summary>
        /// Releases Mojang published no mappings for, by the SHA-1 of their client jar, and the name table Campfire
        /// carries for each (1.7.2 to 1.14.3). Each table names its jar in a "# client &lt;SHA-1&gt;" line.
        /// </summary>
        private static readonly Lazy<Dictionary<string, string>> Legacy = new Lazy<Dictionary<string, string>>(() =>
        {
            var tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var assembly = typeof(GameMappings).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(LegacyPrefix, StringComparison.Ordinal)))
            {
                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    for (string line = reader.ReadLine(); line != null && line.StartsWith("#", StringComparison.Ordinal); line = reader.ReadLine())
                    {
                        if (line.StartsWith("# client ", StringComparison.Ordinal))
                            tables[line.Substring("# client ".Length).Trim()] = resource;
                    }
                }
            }
            return tables;
        });

        /// <summary>Releases before 1.14.4 that the agent still knows, through the tables above.</summary>
        internal static IReadOnlyCollection<string> LegacyVersions => Legacy.Value.Values
            .Select(r => r.Substring(LegacyPrefix.Length, r.Length - LegacyPrefix.Length - ".txt".Length))
            .ToList();

        /// <summary>
        /// Returns the mapping file for the game running as <paramref name="processId"/>, downloading it when
        /// needed, or <c>null</c> when the release has none: it is not obfuscated, or older than 1.14.4.
        /// </summary>
        public static async Task<string> PrepareAsync(int processId, CancellationToken cancellation)
        {
            string commandLine = await Task.Run(() => GameAccount.ReadCommandLine(processId), cancellation).ConfigureAwait(false);
            var source = commandLine == null ? null : await Task.Run(() => FindSource(commandLine), cancellation).ConfigureAwait(false);
            if (source == null)
                return null;

            if (source.Value.Url.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
                return await Task.Run(() => ExtractEmbedded(source.Value.Url.Substring(EmbeddedPrefix.Length)), cancellation).ConfigureAwait(false);

            // 按 SHA-1 命名；只要文件已在缓存里（例如 Windows 侧预下好放进数据目录）就直接用，不联网。
            string path = Path.Combine(Directory, source.Value.Sha1 + ".txt");
            await Gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (File.Exists(path))
                    return path;

                System.IO.Directory.CreateDirectory(Directory);
                Exception last = null;
                foreach (string url in Urls(source.Value.Url))
                {
                    try
                    {
                        byte[] data = await DownloadAsync(url, cancellation).ConfigureAwait(false);
                        if (!string.Equals(Sha1(data), source.Value.Sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            LinkLog.Warn("Mapping file from " + url + " failed its checksum.");
                            continue;
                        }
                        string temporary = path + ".tmp";
                        File.WriteAllBytes(temporary, data);
                        if (File.Exists(path))
                            File.Delete(temporary);
                        else
                            File.Move(temporary, path);
                        LinkLog.Info("Mapping file downloaded from " + url + ".");
                        return path;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested
                                               || ex is IOException)
                    {
                        last = ex;
                    }
                }
                LinkLog.Warn("The mapping file could not be downloaded: " + last?.Message
                             + " (automatic LAN opening will work only with official-name releases)");
                return null;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Finds the version file of the running game and, following "inheritsFrom" to the release it is
        /// based on, the URL and SHA-1 of its client mappings.
        /// </summary>
        internal static (string Url, string Sha1)? FindSource(string commandLine)
        {
            var arguments = GameAccount.Split(commandLine);
            string versionFile = FindVersionFile(arguments);
            if (versionFile == null)
                return null;

            string versions = Path.GetDirectoryName(Path.GetDirectoryName(versionFile));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (string file = versionFile; file != null && seen.Add(file);)
            {
                if (!(ReadJson(file) is Dictionary<string, object> json))
                    return null;
                if (Get(json, "downloads", "client_mappings") is Dictionary<string, object> mappings
                    && mappings.TryGetValue("url", out object url) && url is string link
                    && mappings.TryGetValue("sha1", out object sha) && sha is string sha1)
                    return (link, sha1);
                if (Get(json, "downloads", "client", "sha1") is string client && Legacy.Value.TryGetValue(client, out string resource))
                    return (EmbeddedPrefix + resource, client);

                file = json.TryGetValue("inheritsFrom", out object parent) && parent is string name && name.Length > 0
                    ? Path.Combine(versions, name, name + ".json")
                    : null;
            }
            return null;
        }

        /// <summary>
        /// The version file next to the game jar on the class path, or, when the jar is not there as with
        /// newer Forge, the one named by --version in the folder the libraries come from.
        /// Linux: classpath is ':' separated and launchers use "/libraries/".
        /// </summary>
        private static string FindVersionFile(IReadOnlyList<string> arguments)
        {
            string classPath = ValueAfter(arguments, "-cp") ?? ValueAfter(arguments, "-classpath") ?? ValueAfter(arguments, "--class-path");
            // Windows 用 ';'，Linux 用 ':'。Windows 上不能连 ':' 一起拆，否则盘符 C:\ 会被切断。
            char[] separators = OperatingSystem.IsWindows() ? new[] { ';' } : new[] { ':', ';' };
            var entries = (classPath ?? "")
                .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Where(e => e.Length > 0)
                .ToList();
            foreach (string entry in entries)
            {
                string directory = Path.GetDirectoryName(entry);
                string name = Path.GetFileNameWithoutExtension(entry);
                if (directory == null || !string.Equals(Path.GetFileName(directory), name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFileName(Path.GetDirectoryName(directory)), "versions", StringComparison.OrdinalIgnoreCase))
                    continue;
                string json = Path.Combine(directory, name + ".json");
                if (File.Exists(json))
                    return json;
            }

            string version = ValueAfter(arguments, "--version");
            string marker = OperatingSystem.IsWindows() ? "\\libraries\\" : "/libraries/";
            string library = entries.Concat(arguments).FirstOrDefault(e => e.IndexOf(marker, StringComparison.OrdinalIgnoreCase) > 0);
            if (version == null || library == null)
                return null;
            string root = library.Substring(0, library.IndexOf(marker, StringComparison.OrdinalIgnoreCase));
            int start = root.LastIndexOfAny(new[] { ' ', '=', '"' });
            root = root.Substring(start + 1);
            string candidate = Path.Combine(root, "versions", version, version + ".json");
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>Writes a name table Campfire carries to the data folder, where the agent can read it.</summary>
        private static string ExtractEmbedded(string resource)
        {
            using (var stream = typeof(GameMappings).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                    return null;
                byte[] data;
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    data = memory.ToArray();
                }

                string path = Path.Combine(Directory, Path.GetFileNameWithoutExtension(resource) + "-" + Sha1(data).Substring(0, 12) + ".txt");
                if (!File.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    string temporary = path + ".tmp";
                    File.WriteAllBytes(temporary, data);
                    if (File.Exists(path))
                        File.Delete(temporary);
                    else
                        File.Move(temporary, path);
                }
                return path;
            }
        }

        /// <summary>国内镜像优先，避免校园网访问 Mojang 时整个建房流程卡在超时上。</summary>
        private static IEnumerable<string> Urls(string url)
        {
            if (url.StartsWith(MojangHost, StringComparison.OrdinalIgnoreCase))
                yield return MirrorHost + url.Substring(MojangHost.Length);
            yield return url;
        }

        private static async Task<byte[]> DownloadAsync(string url, CancellationToken cancellation)
        {
            using (var response = await Client.Value.GetAsync(url, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static object ReadJson(string path)
        {
            try
            {
                return Json.TryParse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static object Get(object value, params string[] path)
        {
            foreach (string key in path)
            {
                if (!(value is Dictionary<string, object> map) || !map.TryGetValue(key, out value))
                    return null;
            }
            return value;
        }

        private static string ValueAfter(IReadOnlyList<string> arguments, string option)
        {
            for (int i = 0; i + 1 < arguments.Count; i++)
            {
                if (arguments[i] == option)
                    return arguments[i + 1];
            }
            return null;
        }
    }
}
