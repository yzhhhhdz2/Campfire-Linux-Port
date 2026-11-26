// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新建协议自检。断言只使用共享链接进来的官方源码常量，不改协议本身。

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Campfire.Link;
using Campfire.Link.Room;
using Campfire.Link.Runtime;

namespace Campfire.Cli
{
    internal static class SelfTest
    {
        internal static bool Run()
        {
            int passed = 0;
            int failed = 0;

            Check("RoomCode 往返 / base-34 / 校验位", TestRoomCode, ref passed, ref failed);
            Check("RoomCode 文本容错（大小写 / I→1 / O→0 / 聊天文本扫描）", TestRoomCodeTolerance, ref passed, ref failed);
            Check("RoomFraming 请求帧：1 字节 kind 长度 + 4 字节大端长度", TestRequestFrame, ref passed, ref failed);
            Check("RoomFraming 响应帧：状态 0/32/255 + 往返", TestResponseFrame, ref passed, ref failed);
            Check("RoomFraming 拒绝超过 1 MiB 的 body", TestBodyLimit, ref passed, ref failed);
            Check("协议常量与官方 Windows 客户端一致", TestConstants, ref passed, ref failed);
            Check("EasyTier 版本锁 2.6.4 / Linux 架构 / 非 Win7 分支", TestEasyTierVersion, ref passed, ref failed);
            Check("EasyTierConfig TOML：no_tun / private_mode / QUIC", TestToml, ref passed, ref failed);
            Check("邀请码 → 虚拟网络名/密钥 → TOML 贯通", TestCodeToToml, ref passed, ref failed);
            Check("完整 Link/ 层与 Linux 适配层已入编", TestFullLink, ref passed, ref failed);
            Check("LinkSession 编排层 / 消息泵 / CLI 会话层可用", TestSessionLayer, ref passed, ref failed);

            Console.WriteLine();
            Console.WriteLine("通过 " + passed + " 项，失败 " + failed + " 项。");
            if (failed == 0)
                Console.WriteLine("SELFTEST OK");
            else
                Console.Error.WriteLine("SELFTEST FAILED");

            return failed == 0;
        }

        private static void TestRoomCode()
        {
            for (int i = 0; i < 20; i++)
            {
                RoomCode code = RoomCode.Create();
                Assert(code.Code.Length == 21, "邀请码长度应为 21，实际 " + code.Code.Length);
                Assert(code.Code.StartsWith("U/", StringComparison.Ordinal), "邀请码应以 U/ 开头");
                Assert(Regex.IsMatch(code.Code, @"^U/[0-9A-HJ-NP-Z]{4}(-[0-9A-HJ-NP-Z]{4}){3}$"),
                    "邀请码不符合 base-34 格式：" + code.Code);
                Assert(code.NetworkName.StartsWith("scaffolding-mc-", StringComparison.Ordinal),
                    "虚拟网络名前缀不匹配：" + code.NetworkName);
                Assert(code.NetworkSecret.Length == 9, "网络密钥格式异常：" + code.NetworkSecret);

                // Find() 内部会用 value % 7 == 0 校验；能解析回来即说明校验位正确。
                RoomCode found = RoomCode.Find(code.Code);
                Assert(found != null, "Find 无法解析自己生成的邀请码：" + code.Code);
                Assert(found.Code == code.Code, "Find 还原的邀请码不一致");
                Assert(found.NetworkName == code.NetworkName, "Find 还原的网络名不一致");
                Assert(found.NetworkSecret == code.NetworkSecret, "Find 还原的网络密钥不一致");
            }
        }

        private static void TestRoomCodeTolerance()
        {
            RoomCode code = RoomCode.Create();

            Assert(RoomCode.Find(code.Code.ToLowerInvariant()) != null, "小写邀请码应可解析");

            RoomCode inChat = RoomCode.Find("一起来玩吧 " + code.Code + " 速进");
            Assert(inChat != null && inChat.Code == code.Code, "应能从整段聊天文本中扫出邀请码");

            // 只把正文里真实出现的 1 / 0 换成易混淆的 I / O，前缀和连字符不动。
            string tolerant = null;
            for (int attempt = 0; attempt < 100 && tolerant == null; attempt++)
            {
                RoomCode candidate = RoomCode.Create();
                string body = candidate.Code.Substring(2);
                if (body.Contains('1') || body.Contains('0'))
                {
                    tolerant = "U/" + body.Replace('1', 'I').Replace('0', 'O');
                    code = candidate;
                }
            }
            Assert(tolerant != null, "100 次随机未生成含 1/0 的邀请码（概率极低）");
            RoomCode normalized = RoomCode.Find(tolerant);
            Assert(normalized != null && normalized.Code == code.Code, "I→1 / O→0 容错失败：" + tolerant);
        }

        private static void TestRequestFrame()
        {
            byte[] body = Encoding.UTF8.GetBytes("ping-body-测试");
            const string kind = "c:ping";
            int kindLength = Encoding.UTF8.GetByteCount(kind);

            var stream = new MemoryStream();
            RoomFraming.WriteRequestAsync(stream, kind, body, CancellationToken.None).GetAwaiter().GetResult();
            byte[] frame = stream.ToArray();

            Assert(frame.Length == 1 + kindLength + 4 + body.Length, "请求帧总长度不对");
            Assert(frame[0] == kindLength, "第 1 字节应为 kind 长度");
            Assert(Encoding.UTF8.GetString(frame, 1, kindLength) == kind, "kind 内容不对");
            int offset = 1 + kindLength;
            int declared = (frame[offset] << 24) | (frame[offset + 1] << 16) | (frame[offset + 2] << 8) | frame[offset + 3];
            Assert(declared == body.Length, "4 字节大端 body 长度不对");

            stream.Position = 0;
            RoomFraming.Request request = RoomFraming.ReadRequestAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
            Assert(request != null && request.Kind == kind, "读回的 kind 不对");
            Assert(Same(request.Body, body), "读回的 body 不对");
        }

        private static void TestResponseFrame()
        {
            byte[] body = Encoding.UTF8.GetBytes("response-body");
            byte[] statuses = { RoomFraming.StatusOk, RoomFraming.StatusNotReady, RoomFraming.StatusError };

            foreach (byte status in statuses)
            {
                var stream = new MemoryStream();
                RoomFraming.WriteResponseAsync(stream, status, body, CancellationToken.None).GetAwaiter().GetResult();
                byte[] frame = stream.ToArray();

                Assert(frame.Length == 5 + body.Length, "响应帧总长度不对");
                Assert(frame[0] == status, "响应状态字节不对");
                int declared = (frame[1] << 24) | (frame[2] << 16) | (frame[3] << 8) | frame[4];
                Assert(declared == body.Length, "响应帧的 4 字节大端长度不对");

                stream.Position = 0;
                RoomFraming.Response response = RoomFraming.ReadResponseAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
                Assert(response.Status == status, "读回的状态不对");
                Assert(Same(response.Body, body), "读回的响应 body 不对");
                Assert(response.IsOk == (status == RoomFraming.StatusOk), "IsOk 判定不对");
            }
        }

        private static void TestBodyLimit()
        {
            // 手工构造一个声明 1 MiB + 1 的响应头；WriteResponseAsync 不限制大小，限制在读取侧。
            var header = new byte[5];
            header[0] = RoomFraming.StatusOk;
            header[1] = 0x00;
            header[2] = 0x10;
            header[3] = 0x00;
            header[4] = 0x01;

            var stream = new MemoryStream(header);
            bool rejected = false;
            try
            {
                RoomFraming.ReadResponseAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert(rejected, "超过 1 MiB 的 body 应被拒绝");
        }

        private static void TestConstants()
        {
            Assert(RoomProtocol.HostNamePrefix == "scaffolding-mc-server-", "HostNamePrefix 与官方不一致");
            Assert(RoomProtocol.HostAddress == "10.144.144.1", "HostAddress 与官方不一致");
            Assert(RoomProtocol.Fingerprint.Length == 16, "Fingerprint 长度应为 16");
            Assert(RoomProtocol.Ping == "c:ping", "Ping kind 与官方不一致");
            Assert(RoomProtocol.Protocols == "c:protocols", "Protocols kind 与官方不一致");
            Assert(RoomProtocol.ServerPort == "c:server_port", "ServerPort kind 与官方不一致");
            Assert(RoomFraming.StatusOk == 0 && RoomFraming.StatusNotReady == 32 && RoomFraming.StatusError == 255,
                "状态字应为 0 / 32 / 255");
        }

        private static void TestEasyTierVersion()
        {
            Assert(EasyTierPackage.Version == "2.6.4", "EasyTier 版本必须锁 2.6.4，实际 " + EasyTierPackage.Version);
            Assert(LinuxConfig.EasyTierVersion == EasyTierPackage.Version, "LinuxConfig 与组件版本不一致");
            Assert(EasyTierPackage.IsWindows7 == false, "Linux 分支不能走进 Win7 的 QUIC 关闭分支");
            Assert(!string.IsNullOrEmpty(EasyTierPackage.Architecture), "未识别出 CPU 架构");
            Assert(EasyTierPackage.CorePath.EndsWith("easytier-core", StringComparison.Ordinal),
                "Linux 版 core 不应带 .exe 后缀：" + EasyTierPackage.CorePath);
        }

        private static void TestToml()
        {
            var config = new EasyTierConfig
            {
                NetworkName = "scaffolding-mc-selftest",
                NetworkSecret = "selftest-secret",
                Hostname = "scaffolding-mc-server-selftest",
            };
            string toml = config.ToToml();
            Assert(toml.Contains("no_tun = true"), "TOML 必须保持 no_tun = true（否则需要 root/TUN）");
            Assert(toml.Contains("private_mode = true"), "TOML 必须保持 private_mode = true");
            Assert(toml.Contains("enable_quic_proxy = true"), "非 Win7 分支应启用 QUIC");
            Assert(!toml.Contains("enable_quic_proxy = false"), "不应生成 Win7 专用的 QUIC 关闭项");
            Assert(toml.Contains("network_name = \"scaffolding-mc-selftest\""), "网络名未写入 TOML");
        }

        private static void TestCodeToToml()
        {
            RoomCode code = RoomCode.Create();
            var config = new EasyTierConfig
            {
                NetworkName = code.NetworkName,
                NetworkSecret = code.NetworkSecret,
                Hostname = RoomProtocol.HostNamePrefix + "selftest",
            };
            string toml = config.ToToml();
            Assert(toml.Contains("network_name = \"" + code.NetworkName + "\""), "邀请码换算的网络名没有进入 TOML");
            Assert(toml.Contains("network_secret = \"" + code.NetworkSecret + "\""), "邀请码换算的网络密钥没有进入 TOML");
        }

        private static void TestFullLink()
        {
            var assembly = typeof(RoomCode).Assembly;
            string[] required =
            {
                "Campfire.Link.LinkSettings",
                "Campfire.Link.Runtime.EasyTierInstance",
                "Campfire.Link.Room.RoomServer",
                "Campfire.Link.Room.RoomClient",
                "Campfire.Link.Lobby.LobbyClient",
                "Campfire.Link.Minecraft.GameProxy",
                "Campfire.Link.Runtime.EasyTierPackage",
                "Campfire.AppSettings",
            };
            foreach (string name in required)
                Assert(assembly.GetType(name) != null, "类型没有编译进程序集：" + name);
        }

        private static void TestSessionLayer()
        {
            var assembly = typeof(RoomCode).Assembly;
            var sessionType = assembly.GetType("Campfire.Link.LinkSession");
            Assert(sessionType != null, "LinkSession.cs 本地副本没有编译进程序集");
            var skip = sessionType.GetProperty("SkipProbe", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert(skip != null, "LinkSession.SkipProbe 开关缺失");
            Assert(assembly.GetType("Campfire.Cli.CliHost") != null, "CliHost 未入编");
            Assert(assembly.GetType("Campfire.Cli.SessionState") != null, "SessionState 未入编");

            // 替代 WPF DispatcherSynchronizationContext 的泵必须真的入队 / 出队。
            var pump = new PumpSynchronizationContext();
            int calls = 0;
            pump.Post(_ => calls++, null);
            Assert(calls == 0, "Post 应只入队，不应立即执行");
            Assert(pump.PumpAll() == 1, "PumpAll 应执行队列里的 1 个回调");
            Assert(calls == 1, "PumpAll 没有真正执行回调");

            // 只构造、不启动网络：验证编排层能在没有 WPF Dispatcher 的环境里实例化。
            var session = new LinkSession(pump);
            Assert(session.Stage == LinkStage.Idle, "新会话的初始状态应为 Idle");
            Assert(session.Role == LinkRole.None, "新会话的初始角色应为 None");
        }

        private static void Check(string name, Action test, ref int passed, ref int failed)
        {
            try
            {
                test();
                Console.WriteLine("[OK]   " + name);
                passed++;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[FAIL] " + name + " — " + e.Message);
                failed++;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }
    }
}
