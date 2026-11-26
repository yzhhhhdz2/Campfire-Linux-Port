// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：以 XDG 基础目录规范替代 Windows 的 %LocalAppData% + 注册表，键值存到 settings.ini。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Campfire
{
    /// <summary>
    /// Linux 版的数据目录与偏好设置。原版用 %LocalAppData% 与 Windows 注册表，
    /// 这里改用 XDG_DATA_HOME（默认 ~/.local/share/campfire-linux）。
    /// 可用环境变量 CAMPFIRE_DATA_DIR 覆盖，便于同机跑多个实例做回环测试。
    /// </summary>
    internal static class AppSettings
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _dataDirectory;
        private static bool _loaded;

        public static string DataDirectory
        {
            get
            {
                if (_dataDirectory == null)
                    _dataDirectory = ResolveDataDirectory();
                return _dataDirectory;
            }
        }

        public static string ConfigPath => Path.Combine(DataDirectory, "settings.ini");

        /// <summary>启动早期指定数据目录；须在第一次读取任何设置之前调用。</summary>
        public static void SetDataDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("数据目录不能为空", nameof(path));
            if (_loaded)
                throw new InvalidOperationException("设置已加载，不能再改数据目录。");
            _dataDirectory = Path.GetFullPath(path);
        }

        private static string ResolveDataDirectory()
        {
            string overridden = Environment.GetEnvironmentVariable("CAMPFIRE_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
                return Path.GetFullPath(overridden);

            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
                return Path.Combine(Path.GetFullPath(xdg), "campfire-linux");

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            return Path.Combine(home, ".local", "share", "campfire-linux");
        }

        /// <summary>对应原 AppSettings.Get；不存在的键返回 null。</summary>
        public static string Get(string key)
        {
            if (key == null)
                return null;
            EnsureLoaded();
            lock (Sync)
                return Values.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>当前所有键值，供 config list 使用。</summary>
        public static IReadOnlyDictionary<string, string> Snapshot()
        {
            EnsureLoaded();
            lock (Sync)
                return new Dictionary<string, string>(Values, StringComparer.Ordinal);
        }

        /// <summary>对应原 AppSettings.Set；value 为 null 表示删除该键。</summary>
        public static void Set(string key, string value)
        {
            if (key == null)
                return;
            EnsureLoaded();
            lock (Sync)
            {
                if (value == null)
                    Values.Remove(key);
                else
                    Values[key] = value;
                Save();
            }
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded)
                    return;
                _loaded = true;
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    if (!File.Exists(ConfigPath))
                        return;
                    foreach (string line in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                    {
                        if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                            continue;
                        int split = line.IndexOf('=');
                        if (split <= 0)
                            continue;
                        string key = line.Substring(0, split);
                        string raw = line.Substring(split + 1);
                        try
                        {
                            Values[key] = Uri.UnescapeDataString(raw);
                        }
                        catch (UriFormatException)
                        {
                            Values[key] = raw;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 数据目录不可写时退化为「只内存」，联机仍可继续。
                }
            }
        }

        /// <summary>值做 URL 转义，这样房间公告、节点列表里的换行与中文都能安全存成单行。</summary>
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var text = new StringBuilder();
                text.AppendLine("# Campfire Linux port settings (GPL-3.0 fork of Xocio/Campfire v1.1.32)");
                foreach (var pair in Values)
                    text.Append(pair.Key).Append('=').AppendLine(Uri.EscapeDataString(pair.Value));
                File.WriteAllText(ConfigPath, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 同上：不因写不了配置而中断联机。
            }
        }
    }
}
