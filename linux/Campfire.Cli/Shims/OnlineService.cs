// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：去掉 WPF 的 System.Windows.Media.Color（公告颜色），保留 LobbyClient 所需的网络成员；
//       服务地址改由 LinuxConfig 运行时提供，只接自建大厅，不接触官方 api.mccampfire.top。

using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;

namespace Campfire.Online
{
    /// <summary>
    /// 只实现 LobbyClient.cs 用到的成员。原版的 /v1/manifest 更新检查、公告、统计上报
    /// 在 Linux 分支明确不移植（服务端未开源，且这些功能与邀请码联机无关）。
    /// </summary>
    internal static class OnlineService
    {
        private static readonly Lazy<HttpClient> LazyClient = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Campfire-Linux/" + ShortVersion.ToString(3));
            return client;
        });

        /// <summary>服务地址（无尾斜杠），null 表示未配置、大厅不可用。</summary>
        public static string BaseUrl => LinuxConfig.ServiceUrl;

        public static bool IsAvailable => BaseUrl != null;

        /// <summary>对齐官方 Windows 客户端 v1.1.32，供自建大厅做版本判断。</summary>
        public static Version CurrentVersion => LinuxConfig.AppVersion;

        internal static HttpClient Client => LazyClient.Value;

        internal static CancellationToken WithTimeout(CancellationTokenSource source, int seconds = 10)
        {
            source.CancelAfter(TimeSpan.FromSeconds(seconds));
            return source.Token;
        }

        /// <summary>读 "a.b.c" 形式的三段版本号；其它输入返回 null。</summary>
        internal static Version ParseVersion(object value)
        {
            if (!(value is string text) || text.Length == 0)
                return null;
            string[] parts = text.Split('.');
            if (parts.Length != 3)
                return null;
            var numbers = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            }
            return new Version(numbers[0], numbers[1], numbers[2]);
        }

        internal static Version ShortVersion => LinuxConfig.AppVersion;
    }
}
