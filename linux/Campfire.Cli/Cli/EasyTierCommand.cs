// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增 EasyTier Linux 组件的准备/检查命令。

using System;
using System.IO;
using System.Threading;
using Campfire.Link;
using Campfire.Link.Runtime;

namespace Campfire.Cli
{
    internal static class EasyTierCommand
    {
        internal static int Run(string[] args)
        {
            if (args.Length == 0)
                return Usage();
            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    return Status();
                case "install":
                    return Install(args);
                case "check":
                    return Check();
                case "uninstall":
                    EasyTierPackage.Uninstall();
                    Console.WriteLine("已删除 " + EasyTierPackage.InstallDirectory);
                    return 0;
                default:
                    return Usage();
            }
        }

        private static int Status()
        {
            Console.WriteLine("版本        : " + EasyTierPackage.Version);
            Console.WriteLine("架构        : " + EasyTierPackage.Architecture);
            Console.WriteLine("Release 资产: " + EasyTierPackage.AssetName);
            Console.WriteLine("安装目录    : " + EasyTierPackage.InstallDirectory);
            Console.WriteLine("组件就绪    : " + (EasyTierPackage.IsInstalled() ? "是" : "否"));
            Console.WriteLine();
            Console.WriteLine("下载源（按顺序尝试）：");
            foreach (string url in EasyTierPackage.SourceUrls)
                Console.WriteLine("  " + url);
            Console.WriteLine();
            Console.WriteLine("学校网慢 / 断连时，先用下载工具断点续传下好 zip，再执行：");
            Console.WriteLine("  campfire easytier install --local /path/to/" + EasyTierPackage.AssetName);
            Console.WriteLine("或设置环境变量 CAMPFIRE_EASYTIER_ZIP 指向该文件。");
            Console.WriteLine();
            Console.WriteLine("注意：官方 v2.6.4 Release 没有提供 Linux 资产的校验和文件，");
            Console.WriteLine("      因此本分支不编造哈希；如需校验，请在安装目录放 checksums.sha256。");
            return 0;
        }

        private static int Install(string[] args)
        {
            string local = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--local" && i + 1 < args.Length)
                {
                    local = args[++i];
                }
                else
                {
                    return Usage();
                }
            }
            if (local != null)
            {
                if (!File.Exists(local))
                {
                    Console.Error.WriteLine("找不到压缩包：" + local);
                    return 2;
                }
                Environment.SetEnvironmentVariable("CAMPFIRE_EASYTIER_ZIP", Path.GetFullPath(local));
            }

            try
            {
                Console.WriteLine("准备 EasyTier " + EasyTierPackage.Version + " / " + EasyTierPackage.Architecture + " …");
                EasyTierPackage.InstallAsync(new ConsoleProgress(), CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine();
                Console.WriteLine("组件就绪：");
                Console.WriteLine("  " + EasyTierPackage.CorePath);
                Console.WriteLine("  " + EasyTierPackage.CliPath);
                if (!OperatingSystem.IsLinux())
                    Console.WriteLine("⚠ 当前不是 Linux：这些是 ELF 二进制，只能拷到 Linux 上运行。");
                return 0;
            }
            catch (LinkException ex)
            {
                Console.Error.WriteLine("准备失败：" + ex.LocalizedMessage);
                if (ex.InnerException != null)
                    Console.Error.WriteLine("原因：" + ex.InnerException.Message);
                return 1;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                Console.Error.WriteLine("准备失败：" + ex.Message);
                return 1;
            }
        }

        private static int Check()
        {
            if (!EasyTierPackage.IsInstalled())
            {
                Console.Error.WriteLine("组件尚未安装，先运行 campfire easytier install。");
                return 2;
            }
            if (!OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine("当前不是 Linux，无法执行 Linux 二进制；请拷到 Ubuntu 上再检查。");
                return 2;
            }
            bool ok = EasyTierPackage.CanRunAsync().GetAwaiter().GetResult();
            Console.WriteLine(ok ? "easytier-core 可以启动。" : "easytier-core 启动失败，请查看 " + LinkLog.FilePath);
            return ok ? 0 : 1;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("用法：campfire easytier <status|install [--local <zip>]|check|uninstall>");
            return 2;
        }

        private sealed class ConsoleProgress : IProgress<DownloadProgress>
        {
            private int _lastPercent = -1;

            public void Report(DownloadProgress value)
            {
                if (value.Total <= 0)
                    return;
                int percent = (int)(value.Fraction * 100);
                if (percent == _lastPercent)
                    return;
                _lastPercent = percent;
                Console.Write("\r  下载 " + percent + "% (" + value.Received / 1024 / 1024 + " / " +
                              value.Total / 1024 / 1024 + " MB)");
                if (percent == 100)
                    Console.WriteLine();
            }
        }
    }
}
