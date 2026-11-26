// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增 Linux 分支的运行期配置（服务地址、EasyTier 版本），替代原工程的编译期 AssemblyMetadata。

using System;

namespace Campfire
{
    /// <summary>
    /// Linux 分支的运行期配置。原 Windows 版把服务地址编译进程序集（ServiceUrl），
    /// 这里改成运行时可读，方便同一个二进制既能连自建大厅、也能完全离线使用。
    /// </summary>
    internal static class LinuxConfig
    {
        /// <summary>必须与官方 Windows 客户端一致，否则 EasyTier 网络协议不兼容。</summary>
        public const string EasyTierVersion = "2.6.4";

        /// <summary>与官方 Windows 客户端报告的应用版本一致，供自建大厅判断 minAppVersion。</summary>
        public static readonly Version AppVersion = new Version(1, 1, 32);

        private static string _serviceUrl;
        private static bool _loaded;

        /// <summary>自建联机大厅的地址；null 表示不启用任何在线服务。</summary>
        public static string ServiceUrl
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _serviceUrl = Normalize(Environment.GetEnvironmentVariable("CAMPFIRE_SERVICE_URL")
                                            ?? AppSettings.Get("service.url"));
                }
                return _serviceUrl;
            }
        }

        /// <summary>由 CLI 的 --service-url 覆盖。返回 false 时 error 说明原因。</summary>
        public static bool TrySetServiceUrl(string value, out string error)
        {
            string normalized = Normalize(value);
            if (value != null && value.Trim().Length > 0 && normalized == null)
            {
                error = "服务地址必须是 https，或指向本机的 http（loopback）；收到：" + value;
                return false;
            }
            _serviceUrl = normalized;
            _loaded = true;
            error = null;
            return true;
        }

        /// <summary>对齐原 OnlineService.ReadServiceUrl 的校验规则：https 或 loopback。</summary>
        private static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            string trimmed = url.Trim().TrimEnd('/');
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback)
                ? trimmed
                : null;
        }
    }
}
