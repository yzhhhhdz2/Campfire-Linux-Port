// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新建 CLI 入口与全局选项解析（--data-dir / --service-url）。

using System;
using System.Collections.Generic;
using System.Text;

namespace Campfire.Cli
{
    internal static class Program
    {
        private const string Stage = "阶段 1.5（联机会话 + 自建大厅）";

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is ArgumentException)
            {
                // 某些重定向环境不允许改编码，忽略即可。
            }

            if (!TryParseGlobals(args, out string[] rest, out int exitCode))
                return exitCode;

            if (rest.Length == 0 || rest[0] == "--help" || rest[0] == "-h")
            {
                PrintHelp();
                return 0;
            }

            var tail = new string[rest.Length - 1];
            Array.Copy(rest, 1, tail, 0, tail.Length);

            switch (rest[0].ToLowerInvariant())
            {
                case "selftest":
                    return SelfTest.Run() ? 0 : 1;
                case "version":
                    return VersionCommand.Run(tail);
                case "doctor":
                    return Doctor.Run(tail);
                case "config":
                    return ConfigCommand.Run(tail);
                case "easytier":
                    return EasyTierCommand.Run(tail);
                case "lobby":
                    return LobbyCommand.Run(tail);
                case "code":
                    return SessionCommands.Code(tail);
                case "host":
                    return SessionCommands.Host(tail);
                case "join":
                    return SessionCommands.Join(tail);
                case "status":
                    return SessionCommands.Status(tail);
                case "peers":
                    return SessionCommands.Peers(tail);
                case "logs":
                    return SessionCommands.Logs(tail);
                case "stop":
                    return SessionCommands.Stop(tail);
                default:
                    Console.Error.WriteLine("未知命令：" + rest[0]);
                    Console.Error.WriteLine("运行 campfire --help 查看可用命令。");
                    return 2;
            }
        }

        /// <summary>全局选项可出现在任意位置，解析后从命令参数里剥离。</summary>
        private static bool TryParseGlobals(string[] args, out string[] rest, out int exitCode)
        {
            var remaining = new List<string>();
            exitCode = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--data-dir" || arg == "--service-url")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine(arg + " 需要一个参数值。");
                        rest = null;
                        exitCode = 2;
                        return false;
                    }
                    string value = args[++i];
                    if (arg == "--data-dir")
                    {
                        try
                        {
                            AppSettings.SetDataDirectory(value);
                        }
                        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                        {
                            Console.Error.WriteLine("--data-dir 无效：" + ex.Message);
                            rest = null;
                            exitCode = 2;
                            return false;
                        }
                    }
                    else if (!LinuxConfig.TrySetServiceUrl(value, out string error))
                    {
                        Console.Error.WriteLine("--service-url " + error);
                        rest = null;
                        exitCode = 2;
                        return false;
                    }
                    continue;
                }
                remaining.Add(arg);
            }
            rest = remaining.ToArray();
            return true;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Campfire Linux port — " + Stage);
            Console.WriteLine("源码基线：Xocio/Campfire v1.1.32 (GPL-3.0)，原 Windows 工程零改动。");
            Console.WriteLine();
            Console.WriteLine("用法：campfire [全局选项] <命令> [参数]");
            Console.WriteLine();
            Console.WriteLine("全局选项：");
            Console.WriteLine("  --data-dir <目录>      覆盖 XDG 数据目录（同机跑多个实例做回环测试）");
            Console.WriteLine("  --service-url <地址>   自建联机大厅地址（https 或指向本机的 http）");
            Console.WriteLine();
            Console.WriteLine("命令：");
            Console.WriteLine("  selftest               协议自检：邀请码、帧格式、TOML、版本锁、类型入编");
            Console.WriteLine("  version                版本与构建信息");
            Console.WriteLine("  doctor                 环境自检：平台、数据目录、EasyTier 组件、服务地址");
            Console.WriteLine("  config <path|list|get|set|unset>   读写 XDG 数据目录里的设置");
            Console.WriteLine("  easytier <status|install|check|uninstall>   准备 EasyTier 2.6.4 Linux 组件");
            Console.WriteLine("  lobby <status|list|publish|join>   自建大厅（需 --service-url 或 config set service.url）");
            Console.WriteLine("  code [--check <邀请码>]  生成合法邀请码 / 校验并展开邀请码");
            Console.WriteLine("  --help                 显示本帮助");
            Console.WriteLine();
            Console.WriteLine("联机会话（须在 Linux 上运行；host/join 是前台长驻进程）：");
            Console.WriteLine("  host --port <端口> [--player 名] [--skip-probe] [--node 地址]");
            Console.WriteLine("       [--hide-ip] [--no-relay] [--share-lan] [--timeout 30|60|120]");
            Console.WriteLine("       建房并打印邀请码。请先在 MC 里点「对局域网开放」，端口与 --port 一致。");
            Console.WriteLine("  join --code <U/XXXX-XXXX-XXXX-XXXX> [--player 名] [--local-port N] [--skip-probe]");
            Console.WriteLine("       加入房间，打印本地端口；MC 连 127.0.0.1:<本地端口>。");
            Console.WriteLine("  status / peers / logs [--lines 100] / stop");
            Console.WriteLine("       查询会话、节点、日志，或请求前台进程停止。须与 host/join 用同一个 --data-dir。");
            Console.WriteLine();
            Console.WriteLine("自建大厅（服务端在 linux/Campfire.Lobby/，先 dotnet run 启动）：");
            Console.WriteLine("  lobby list                              列出公开房间");
            Console.WriteLine("  lobby publish --code <邀请码> [--title 标题] [--password 密码] [--once]");
            Console.WriteLine("       公开已有房间，Ctrl+C 撤下；--once 发布后立即撤下（自检用）。");
            Console.WriteLine("  lobby join <房间ID> [--password 密码]   换取邀请码，再交给上面的 join");
            Console.WriteLine();
            Console.WriteLine("--skip-probe：跳过 Minecraft 世界探测，供没有真实游戏世界时做回环自检。");
            Console.WriteLine();
            Console.WriteLine("尚未实现（阶段 2）：Avalonia 界面（须先通过 Linux ⇄ 官方 Windows 客户端双向互通）。");
            Console.WriteLine();
            Console.WriteLine("构建：dotnet build -c Release");
        }
    }
}
