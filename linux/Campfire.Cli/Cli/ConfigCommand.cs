// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增 XDG 配置读写命令，替代 Windows 注册表。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Campfire.Cli
{
    /// <summary>对应原版「设置」页里由 AppSettings 持久化的键值。</summary>
    internal static class ConfigCommand
    {
        internal static int Run(string[] args)
        {
            if (args.Length == 0)
                return Usage();

            switch (args[0].ToLowerInvariant())
            {
                case "path":
                    Console.WriteLine(AppSettings.ConfigPath);
                    return 0;

                case "list":
                    var all = AppSettings.Snapshot();
                    if (all.Count == 0)
                    {
                        Console.WriteLine("（还没有保存任何设置；默认值在原 LinkSettings.cs 里）");
                        return 0;
                    }
                    foreach (var pair in all.OrderBy(p => p.Key, StringComparer.Ordinal))
                        Console.WriteLine(pair.Key + "=" + pair.Value);
                    return 0;

                case "get":
                    if (args.Length < 2)
                        return Usage();
                    string value = AppSettings.Get(args[1]);
                    Console.WriteLine(value ?? "(未设置)");
                    return 0;

                case "set":
                    if (args.Length < 3)
                        return Usage();
                    AppSettings.Set(args[1], string.Join(" ", args.Skip(2)));
                    Console.WriteLine("已保存 " + args[1]);
                    return 0;

                case "unset":
                    if (args.Length < 2)
                        return Usage();
                    AppSettings.Set(args[1], null);
                    Console.WriteLine("已删除 " + args[1]);
                    return 0;

                default:
                    return Usage();
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("用法：campfire config <path|list|get <键>|set <键> <值>|unset <键>>");
            Console.Error.WriteLine();
            Console.Error.WriteLine("常用键：");
            Console.Error.WriteLine("  service.url       自建联机大厅地址，例如 http://127.0.0.1:5080");
            Console.Error.WriteLine("  link.player       房间内显示的玩家名");
            Console.Error.WriteLine("  link.relay        1/0，是否允许走中转节点");
            Console.Error.WriteLine("  link.hideip       1/0，是否隐藏本机地址");
            Console.Error.WriteLine("  link.publicnodes  1/0，是否使用内置公共节点");
            Console.Error.WriteLine("  link.nodes        自定义节点，空格/逗号分隔");
            Console.Error.WriteLine("  link.sharemods    0（Linux 分支裁掉了模组同步，建议保持 0）");
            Console.Error.WriteLine("  link.backup       0（第一阶段不做世界备份）");
            return 2;
        }
    }
}
