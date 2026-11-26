// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：模组同步 / 语音功能按移植计划裁掉，此处保留协议与编排层所需的最小类型。
//       RoomServer/RoomClient 的 campfire:mods 与 campfire:voice 请求仍按原协议应答，
//       房主侧统一回「模组不可用」，官方 Windows 客户端会优雅降级，不影响邀请码联机。
//       所有新增成员只提供安全的空值，绝不改动协议常量。

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Worlds;

namespace Campfire.Mods
{
    /// <summary>仅 LinkSettings 需要一个可持久化枚举值；Linux 分支不使用模组同步。</summary>
    internal enum ModSource
    {
        OfficialFirst,
        OfficialOnly,
        HostFirst,
    }

    /// <summary>空模组列表。RoomClient.GetModListAsync 会得到一个「没有模组」的空对象。</summary>
    internal sealed class ModList
    {
        public string Signature => "";
        public string GameVersion => "";
        public VersionLoader? Loader => null;
        public IReadOnlyList<ModInfo> Mods => Array.Empty<ModInfo>();

        public static ModList FromJson(object json)
        {
            return json is Dictionary<string, object> ? new ModList() : null;
        }
    }

    internal sealed class ModInfo
    {
        public string Sha1 => "";
    }

    /// <summary>
    /// 房主侧不提供模组文件：ListJson 恒为空 JSON，ReadChunk 恒返回 null，
    /// PrepareAsync 返回 null 让 LinkSession 把 RoomServer.ModsUnavailable 置为 true（原版标准降级路径）。
    /// </summary>
    internal sealed class ModHost
    {
        public ModList List => new ModList();

        public byte[] ListJson => Encoding.UTF8.GetBytes("{}");

        public byte[] ReadChunk(string sha1, long offset) => null;

        public static Task<ModHost> PrepareAsync(int gamePort, bool servesFiles)
            => Task.FromResult<ModHost>(null);
    }

    internal sealed class ModSyncException : Exception
    {
        public ModSyncException(string key, Exception inner = null) : base(key, inner)
        {
            Key = key;
        }

        public string Key { get; }
    }

    internal enum VoiceInstall
    {
        Ready,
        Installing,
        Unavailable,
    }

    /// <summary>语音模组已裁剪，所有方法返回「不可用」，不读写游戏目录、不联网。</summary>
    internal static class VoiceMod
    {
        public static ModInfo Find(string modsDirectory) => null;

        public static Task<(VoiceInstall Result, ModInfo Mod)> InstallAsync(
            string modsDirectory, string gameVersion, VersionLoader? loader,
            ModInfo preferred, CancellationToken cancellation)
            => Task.FromResult((VoiceInstall.Unavailable, (ModInfo)null));

        public static bool EnableLocalServer(string gameDirectory) => false;

        public static int? ReadServerPort(string gameDirectory) => null;
    }

    /// <summary>语音功能已裁剪，但 RoomServer.Voice / RoomClient.GetVoiceAsync 仍需要这个类型。</summary>
    internal sealed class VoiceOffer
    {
        public VoiceOffer()
        {
        }

        public VoiceOffer(bool ready, int port, string gameVersion, VersionLoader? loader, ModInfo mod)
        {
            Ready = ready;
            Port = port;
            GameVersion = gameVersion;
            Loader = loader;
            Mod = mod;
        }

        public bool Ready { get; }
        public int Port { get; }
        public string GameVersion { get; }
        public VersionLoader? Loader { get; }
        public ModInfo Mod { get; }

        public static VoiceOffer FromJson(object json)
        {
            return json is Dictionary<string, object> ? new VoiceOffer() : null;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>();
    }
}
