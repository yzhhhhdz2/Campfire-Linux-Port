// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增版本信息命令。

using System;

namespace Campfire.Cli
{
    internal static class VersionCommand
    {
        internal static int Run(string[] args)
        {
            Console.WriteLine("Campfire Linux port / 阶段 1.5");
            Console.WriteLine("源码基线   : Xocio/Campfire v1.1.32 (GPL-3.0)");
            Console.WriteLine("应用版本   : " + LinuxConfig.AppVersion.ToString(3) + "（与官方 Windows 客户端对齐）");
            Console.WriteLine("EasyTier   : " + Campfire.Link.Runtime.EasyTierPackage.Version + " / " +
                              Campfire.Link.Runtime.EasyTierPackage.Architecture);
            Console.WriteLine(".NET       : " + Environment.Version);
            Console.WriteLine("数据目录   : " + AppSettings.DataDirectory);
            Console.WriteLine("服务地址   : " + (LinuxConfig.ServiceUrl ?? "未配置（大厅不可用，邀请码联机不受影响）"));
            return 0;
        }
    }
}
