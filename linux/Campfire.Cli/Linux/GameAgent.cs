// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：把原 Link/Minecraft/JavaAgent.cs 的「注入 Java Agent 自动开放局域网世界」移植到 Linux。
//       原版 C# 侧唯一平台相关的是找 java.exe（硬编码 Windows 路径 / java.exe / Program Files）；
//       这里换成 Linux 候选（JAVA_HOME、CAMPFIRE_ATTACH_JAVA、/usr/lib/jvm、启动器 runtime、PATH），
//       其余「解出 3 个 jar → 另起一个带 jdk.attach 的 java → VirtualMachine.attach(pid)」
//       与原版逐字等价，Java 侧的 CampfireAgent/Attach/Hook 本身完全跨平台。
//       LanOpener / AgentState 是与游戏内 Hook 约定的纯文本状态文件协议，零平台依赖，
//       逻辑照抄原 LanOpener.cs 与 JavaAgent.cs 的 AgentState，仅把 Text.Get 换成固定中文。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// 向正在运行的 Minecraft JVM 注入 CampfireAgent.jar。注入后游戏内的 Hook 会读
    /// lan-&lt;pid&gt;.txt，等房主进入世界后反射调用游戏自己的 publishServer(...)，
    /// 也就是「对局域网开放」按钮背后的那个方法，再把端口写回 lan-&lt;pid&gt;.done。
    /// </summary>
    internal static class JavaAgent
    {
        private const string AgentResource = "Campfire.CampfireAgent.jar";
        private const string AttachResource = "Campfire.CampfireAttach.jar";
        private const string HookResource = "Campfire.CampfireHook.jar";

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(6);
        private static readonly Dictionary<string, bool> AttachSupport =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        internal static string StatePath(int pid) =>
            Path.Combine(LinkSettings.DataDirectory, "overlay-" + pid + ".txt");

        internal static string ReadyPath(int pid) =>
            Path.Combine(LinkSettings.DataDirectory, "overlay-" + pid + ".ready");

        public static async Task<bool> AttachAsync(int processId, CancellationToken cancellation)
        {
            if (!await LoadAsync(processId, cancellation).ConfigureAwait(false))
                return false;
            try
            {
                var deadline = DateTime.UtcNow + ReadyTimeout;
                while (!File.Exists(ReadyPath(processId)))
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        LinkLog.Warn("The Java agent was loaded, but the game did not start the overlay; see agent.log.");
                        return false;
                    }
                    await Task.Delay(250, cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { return false; }
            LinkLog.Info("In-game overlay agent attached to game process " + processId + ".");
            return true;
        }

        public static async Task<bool> LoadAsync(int processId, CancellationToken cancellation)
        {
            if (processId <= 0)
                return false;
            if (!OperatingSystem.IsLinux())
            {
                LinkLog.Warn("Java agent attach is only supported on Linux in this port.");
                return false;
            }
            if (File.Exists(ReadyPath(processId)))
                return true;   // 这个游戏已经注入过了

            // 混淆版 MC（1.14.4–1.21.11）需要官方 client_mappings，Agent 才能按官方名
            // 反射找到 publishServer / tickServer。未混淆版本 PrepareAsync 返回 null，
            // Agent 会先按原名查找（见 java-agent/src/CampfireNames.java:24）。
            string mappings = null;
            try
            {
                mappings = await GameMappings.PrepareAsync(processId, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                LinkLog.Warn("The mapping file could not be prepared: " + ex.Message);
            }

            await Gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                string java = await Task.Run(FindAttachJava, cancellation).ConfigureAwait(false);
                if (java == null)
                {
                    LinkLog.Warn("No Java with the jdk.attach module was found. Automatic LAN opening needs a full JDK; "
                               + "set CAMPFIRE_ATTACH_JAVA=/path/to/jdk/bin/java or JAVA_HOME.");
                    return false;
                }

                string directory = Path.Combine(LinkSettings.DataDirectory, "agent");
                Directory.CreateDirectory(directory);
                string agent = Extract(AgentResource, Path.Combine(directory, "CampfireAgent.jar"));
                string attach = Extract(AttachResource, Path.Combine(directory, "CampfireAttach.jar"));
                string hook = Extract(HookResource, Path.Combine(directory, "CampfireHook.jar"));
                if (agent == null || attach == null || hook == null)
                {
                    LinkLog.Warn("Campfire agent jars are missing from this build; automatic LAN opening is unavailable.");
                    return false;
                }

                // 数据目录是 Java Hook 读写 lan-<pid>.txt / .done / overlay-* 的唯一信道。
                // 选项以 '|' 分隔，与 java-agent/src/CampfireAgent.java:30 的解析一致。
                string options = "state=" + LinkSettings.DataDirectory
                                 + (mappings != null ? "|mappings=" + mappings : "");
                var startInfo = new ProcessStartInfo(java)
                {
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // ArgumentList 逐项传参，避免 Linux 路径里的空格被 shell 拆开。
                startInfo.ArgumentList.Add("--add-modules");
                startInfo.ArgumentList.Add("jdk.attach");
                startInfo.ArgumentList.Add("-cp");
                startInfo.ArgumentList.Add(attach);
                startInfo.ArgumentList.Add("campfire.CampfireAttach");
                startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add(agent);
                startInfo.ArgumentList.Add(options);

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    bool exited = await Task.Run(() => process.WaitForExit(20000), cancellation).ConfigureAwait(false);
                    if (!exited)
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { }
                        LinkLog.Warn("The Java agent did not finish attaching in time.");
                        return false;
                    }
                    string errorText = await error.ConfigureAwait(false);
                    await output.ConfigureAwait(false);
                    if (process.ExitCode != 0)
                    {
                        LinkLog.Warn("The Java agent could not be attached: " + errorText.Trim());
                        return false;
                    }
                }

                LinkLog.Info("Java agent loaded into game process " + processId + " via " + java + ".");
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LinkLog.Warn("The Java agent could not be attached: " + ex.Message);
                return false;
            }
            finally { Gate.Release(); }
        }

        /// <summary>找一个带 jdk.attach 模块的 java。MC 启动器自带的常是 JRE，没有这个模块。</summary>
        private static string FindAttachJava()
        {
            var candidates = new List<string>();

            string explicitJava = Environment.GetEnvironmentVariable("CAMPFIRE_ATTACH_JAVA");
            if (!string.IsNullOrWhiteSpace(explicitJava))
                candidates.Add(explicitJava.Trim());

            string home = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(home))
                candidates.Add(Path.Combine(home.Trim(), "bin", "java"));

            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                "/usr/lib/jvm", "/usr/java", "/opt/java", "/opt/jdk",
                Path.Combine(user, ".minecraft", "runtime"),
                Path.Combine(user, ".local", "share", "PrismLauncher", "java"),
                Path.Combine(user, ".local", "share", "PolyMC", "java"),
                Path.Combine(user, ".jdks"),
            };
            foreach (string root in roots)
            {
                try
                {
                    if (Directory.Exists(root))
                        candidates.AddRange(Directory.EnumerateFiles(root, "java", SearchOption.AllDirectories));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            candidates.Add("/usr/bin/java");
            candidates.Add("/bin/java");
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(dir))
                    candidates.Add(Path.Combine(dir.Trim(), "java"));

            return candidates.Where(c => !string.IsNullOrEmpty(c))
                             .Distinct(StringComparer.Ordinal)
                             .FirstOrDefault(CanAttach);
        }

        private static bool CanAttach(string java)
        {
            lock (AttachSupport)
                if (AttachSupport.TryGetValue(java, out bool known))
                    return known;

            bool supported = false;
            try
            {
                if (File.Exists(java))
                {
                    var startInfo = new ProcessStartInfo(java)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    startInfo.ArgumentList.Add("--list-modules");
                    using (var process = Process.Start(startInfo))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(10000);
                        supported = output.IndexOf("jdk.attach@", StringComparison.Ordinal) >= 0;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException
                                       || ex is System.ComponentModel.Win32Exception || ex is AggregateException)
            {
            }

            lock (AttachSupport) AttachSupport[java] = supported;
            return supported;
        }

        /// <summary>把内嵌的 jar 写进数据目录（内容相同就不重写）。</summary>
        private static string Extract(string name, string path)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    return null;
                byte[] content;
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    content = buffer.ToArray();
                }
                if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(content))
                    return path;
                string temporary = path + ".tmp";
                File.WriteAllBytes(temporary, content);
                File.Copy(temporary, path, true);
                File.Delete(temporary);
                return path;
            }
        }
    }

    /// <summary>房主房间状态文件，每 3 秒刷新一次；Hook 靠时间戳判断房间是否还开着。</summary>
    internal sealed class AgentState : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string _readyPath;
        private readonly Timer _timer;
        private bool _disposed;
        private volatile string _notice;

        public string Notice
        {
            get => _notice;
            set { _notice = (value ?? "").Replace('\r', ' ').Replace('\n', ' '); Write(); }
        }

        public AgentState(int processId)
        {
            _path = JavaAgent.StatePath(processId);
            _readyPath = JavaAgent.ReadyPath(processId);
            TryDelete(_readyPath);
            Write();
            _timer = new Timer(_ => Write(), null, Interval, Interval);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _timer.Dispose();
            TryDelete(_path);
            TryDelete(_readyPath);
        }

        private void Write()
        {
            var lines = new[]
            {
                "time=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "tabTitle=Campfire 联机",
                "welcome=已加入 Campfire 房间",
                "host=房主", "online=在线", "players=玩家", "latency=延迟",
                "latencyValue={0} ms", "measuring=测量中",
                "noticeTitle=公告", "notice=" + (_notice ?? ""),
            };
            lock (_sync)
            {
                if (_disposed) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    string temporary = _path + ".tmp";
                    File.WriteAllText(temporary, string.Join("\n", lines) + "\n", Utf8);
                    if (File.Exists(_path)) File.Replace(temporary, _path, null);
                    else File.Move(temporary, _path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 请求游戏内 Hook 自动开放局域网世界。协议照抄原 LanOpener.cs：
    /// lan-&lt;pid&gt;.txt 每 3 秒刷新，Hook 写 lan-&lt;pid&gt;.done（port=N / already / 0）。
    /// </summary>
    internal sealed class LanOpener : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string _resultPath;
        private readonly string _world;
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly Timer _timer;
        private bool _disposed;

        public LanOpener(int processId, string worldFolder)
        {
            ProcessId = processId;
            _world = worldFolder ?? "*";
            _path = Path.Combine(LinkSettings.DataDirectory, "lan-" + processId + ".txt");
            _resultPath = Path.Combine(LinkSettings.DataDirectory, "lan-" + processId + ".done");
            TryDelete(_resultPath);
            Write();
            _timer = new Timer(_ => Write(), null, Interval, Interval);
        }

        public int ProcessId { get; }

        /// <summary>null=等待中，正数=端口，0=本来就开着，-1=游戏没能开放。</summary>
        public int? ReadResult()
        {
            try
            {
                if (!File.Exists(_resultPath)) return null;
                string text = File.ReadAllText(_resultPath, Utf8).Trim();
                if (!text.StartsWith("port=", StringComparison.Ordinal)) return -1;
                string value = text.Substring("port=".Length);
                if (value == "already") return 0;
                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port > 0
                    ? port : -1;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _timer.Dispose();
            TryDelete(_path);
            TryDelete(_resultPath);
        }

        private void Write()
        {
            var lines = new[]
            {
                "time=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "world=" + _world,
                "id=" + _id,
                "opened=Campfire 已自动对局域网开放，端口 {0}",
            };
            lock (_sync)
            {
                if (_disposed) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    string temporary = _path + ".tmp";
                    File.WriteAllText(temporary, string.Join("\n", lines) + "\n", Utf8);
                    if (File.Exists(_path)) File.Replace(temporary, _path, null);
                    else File.Move(temporary, _path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
