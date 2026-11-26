// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：匿名使用统计依赖未开源的官方服务端，Linux 分支不移植；
//       LinkSession.cs 里的 Telemetry.Count("link.host"/"link.join") 由本空实现吸收，不发起任何网络请求。

namespace Campfire.Online
{
    internal static class Telemetry
    {
        public static void Count(string feature)
        {
        }
    }
}
