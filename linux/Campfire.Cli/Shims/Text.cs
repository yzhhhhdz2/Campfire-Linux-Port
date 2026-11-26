// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：CLI 阶段不加载原工程的 WPF 本地化字典，改为轻量英文回退；界面阶段再替换为完整文案。

using System;
using System.Globalization;

namespace Campfire.Localization
{
    /// <summary>
    /// 只提供 Link/ 层编译所需的 Text.Get / Text.Format。
    /// 未知键直接返回键名，不会抛异常；协议与网络代码不依赖具体文案。
    /// </summary>
    internal static class Text
    {
        public static string Get(string key) => Fallback(key);

        public static string Format(string key, params object[] args)
        {
            string pattern = Fallback(key);
            if (args == null || args.Length == 0)
                return pattern;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }

        private static string Fallback(string key)
        {
            switch (key)
            {
                case "Lobby.DefaultTitle":
                    return "{0}'s room";
                case "Link.Game.Welcome":
                    return "Connected to a Campfire room.";
                default:
                    return key ?? "";
            }
        }
    }
}
