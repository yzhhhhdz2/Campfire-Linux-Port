// Campfire Linux port — 新增文件，依据 Xocio/Campfire v1.1.32 (GPL-3.0) 的 LobbyClient.cs 契约新建。
// 修改：把心跳/刷新/上限等服务端策略做成配置，便于本机验证与后续部署；默认只监听 loopback。

using System.Globalization;

namespace Campfire.Lobby;

/// <summary>
/// 读取 appsettings.json 的 "Lobby" 段；也支持环境变量覆盖（例如 Lobby__HeartbeatSeconds=90）。
/// 所有数值都做钳制，避免配置写错导致客户端被 LobbyClient 的 30..3600 秒钳制规则意外降级。
/// </summary>
internal sealed class LobbyOptions
{
    public bool Enabled { get; private set; } = true;
    public string Notice { get; private set; } = "";
    /// <summary>管理员移除房间的令牌；留空表示管理端点完全关闭。</summary>
    public string AdminToken { get; private set; } = "";
    /// <summary>"a.b.c" 三段；空字符串表示不限制版本。</summary>
    public string MinAppVersion { get; private set; } = "";
    public int RefreshSeconds { get; private set; } = 30;
    public int HeartbeatSeconds { get; private set; } = 60;
    /// <summary>心跳缺席超过这个秒数即判过期；必须明显大于 HeartbeatSeconds。</summary>
    public int ExpireSeconds { get; private set; } = 180;
    public int MaxRooms { get; private set; } = 200;
    /// <summary>同一来源最多能同时公开几个房间，超过返回 429。</summary>
    public int MaxRoomsPerAddress { get; private set; } = 5;
    public int ReportThreshold { get; private set; } = 3;
    public string[] Tags { get; private set; } = { "生存", "创造", "建筑", "小游戏", "模组", "原版" };
    public string[] BannedOwners { get; private set; } = Array.Empty<string>();
    /// <summary>命中即返回 422 并指出被拒字段；默认空表 = 不做内容过滤。</summary>
    public string[] BlockedWords { get; private set; } = Array.Empty<string>();

    public static LobbyOptions Load(IConfiguration configuration)
    {
        IConfiguration s = configuration.GetSection("Lobby");
        var o = new LobbyOptions
        {
            Enabled = ReadBool(s, "Enabled", true),
            Notice = ReadString(s, "Notice", ""),
            AdminToken = ReadString(s, "AdminToken", ""),
            MinAppVersion = ReadString(s, "MinAppVersion", ""),
            RefreshSeconds = Math.Clamp(ReadInt(s, "RefreshSeconds", 30), 5, 3600),
            HeartbeatSeconds = Math.Clamp(ReadInt(s, "HeartbeatSeconds", 60), 30, 3600),
            MaxRooms = Math.Clamp(ReadInt(s, "MaxRooms", 200), 1, 100000),
            MaxRoomsPerAddress = Math.Clamp(ReadInt(s, "MaxRoomsPerAddress", 5), 1, 1000),
            ReportThreshold = Math.Clamp(ReadInt(s, "ReportThreshold", 3), 1, 1000),
            Tags = ReadArray(s, "Tags", new[] { "生存", "创造", "建筑", "小游戏", "模组", "原版" }),
            BannedOwners = ReadArray(s, "BannedOwners", Array.Empty<string>()),
            BlockedWords = ReadArray(s, "BlockedWords", Array.Empty<string>()),
        };
        // 过期窗口至少是心跳间隔的 2 倍，否则正常心跳之间房间就被清掉了。
        o.ExpireSeconds = Math.Max(ReadInt(s, "ExpireSeconds", 180), o.HeartbeatSeconds * 2);
        return o;
    }

    private static string ReadString(IConfiguration s, string key, string fallback) => s[key] ?? fallback;

    private static int ReadInt(IConfiguration s, string key, int fallback) =>
        int.TryParse(s[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static bool ReadBool(IConfiguration s, string key, bool fallback) =>
        bool.TryParse(s[key], out bool value) ? value : fallback;

    private static string[] ReadArray(IConfiguration s, string key, string[] fallback)
    {
        IConfigurationSection section = s.GetSection(key);
        var children = section.GetChildren().ToArray();
        if (children.Length > 0)
            return children.Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        string text = s[key];
        return string.IsNullOrWhiteSpace(text)
            ? fallback
            : text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }
}
