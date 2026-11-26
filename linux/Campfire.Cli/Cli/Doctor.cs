// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增环境自检命令，把「能不能真在 Linux 上联机」逐项列出来。

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Campfire.Cli
{
    internal static class Doctor
    {
        internal static int Run(string[] args)
        {
            int problems = 0;

            Console.WriteLine("=== 平台 ===");
            Console.WriteLine("  系统        : " + RuntimeInformation.OSDescription);
            Console.WriteLine("  是 Linux    : " + (OperatingSystem.IsLinux() ? "是" : "否"));
            Console.WriteLine("  CPU 架构    : " + RuntimeInformation.OSArchitecture + " → " +
                              Campfire.Link.Runtime.EasyTierPackage.Architecture);
            Console.WriteLine("  .NET        : " + Environment.Version);
            if (!OperatingSystem.IsLinux())
            {
                Console.WriteLine("  ⚠ 当前不是 Linux：这里只能编译与自检，host/join 与互通验证必须在 Ubuntu 上做。");
                problems++;
            }

            Console.WriteLine();
            Console.WriteLine("=== 数据目录（XDG）===");
            Console.WriteLine("  路径        : " + AppSettings.DataDirectory);
            Console.WriteLine("  配置文件    : " + AppSettings.ConfigPath);
            Console.WriteLine("  可写        : " + (CanWrite(AppSettings.DataDirectory) ? "是" : "否"));
            if (!CanWrite(AppSettings.DataDirectory))
                problems++;

            Console.WriteLine();
            Console.WriteLine("=== 在线服务（自建大厅）===");
            Console.WriteLine("  服务地址    : " + (LinuxConfig.ServiceUrl ?? "未配置"));
            Console.WriteLine("  大厅客户端  : " + (Campfire.Link.Lobby.LobbyClient.IsAvailable ? "可用" : "禁用"));
            Console.WriteLine("  邀请码联机  : 与大厅无关，始终可用（走 EasyTier 公共节点，不经服务端）");

            Console.WriteLine();
            Console.WriteLine("=== EasyTier 组件 ===");
            Console.WriteLine("  版本        : " + Campfire.Link.Runtime.EasyTierPackage.Version);
            Console.WriteLine("  Release 资产: " + Campfire.Link.Runtime.EasyTierPackage.AssetName);
            Console.WriteLine("  安装目录    : " + Campfire.Link.Runtime.EasyTierPackage.InstallDirectory);
            bool core = File.Exists(Campfire.Link.Runtime.EasyTierPackage.CorePath);
            bool cli = File.Exists(Campfire.Link.Runtime.EasyTierPackage.CliPath);
            Console.WriteLine("  easytier-core : " + Describe(Campfire.Link.Runtime.EasyTierPackage.CorePath, core));
            Console.WriteLine("  easytier-cli  : " + Describe(Campfire.Link.Runtime.EasyTierPackage.CliPath, cli));
            Console.WriteLine("  组件就绪    : " + (Campfire.Link.Runtime.EasyTierPackage.IsInstalled() ? "是" : "否"));
            if (!Campfire.Link.Runtime.EasyTierPackage.IsInstalled())
                problems++;

            Console.WriteLine();
            Console.WriteLine("=== 编译内容 ===");
            Console.WriteLine("  完整 Link/ 层: " + (HasType("Campfire.Link.Runtime.EasyTierInstance") ? "已入编" : "缺失"));
            Console.WriteLine("  房间协议层  : " + (HasType("Campfire.Link.Room.RoomServer") ? "已入编" : "缺失"));
            Console.WriteLine("  Linux 适配层: " + (HasType("Campfire.Link.Runtime.ChildProcessJob") ? "已入编" : "缺失"));
            Console.WriteLine("  编排层      : " + (HasType("Campfire.Link.LinkSession") ? "已入编" : "缺失"));
            Console.WriteLine("  CLI 会话层  : " + (HasType("Campfire.Cli.CliHost") ? "已入编" : "缺失"));

            Console.WriteLine();
            Console.WriteLine(problems == 0
                ? "结论：环境就绪，可以进行回环测试与官方 Windows 客户端互通验证。"
                : "结论：还有 " + problems + " 项待处理（Windows 上开发属正常，到 Linux 上再跑一遍 doctor）。");
            return 0;
        }

        private static bool HasType(string name)
        {
            return typeof(Campfire.Link.RoomCode).Assembly.GetType(name) != null;
        }

        private static bool CanWrite(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string Describe(string path, bool exists)
        {
            if (!exists)
                return "缺失";
            if (OperatingSystem.IsWindows())
                return "存在（Windows 上无法验证可执行位）";
            try
            {
                bool executable = (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
                return executable ? "存在，可执行" : "存在，但缺少可执行位（chmod +x）";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
            {
                return "存在，无法读取权限";
            }
        }
    }
}
