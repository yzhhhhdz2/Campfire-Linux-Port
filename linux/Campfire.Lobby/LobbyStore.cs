// Campfire Linux port — 新增文件，依据 Xocio/Campfire v1.1.32 (GPL-3.0) 的
// src/Campfire/Link/Lobby/LobbyClient.cs 与 LobbyPublisher.cs 契约新建。
// 修改：用内存字典 + 心跳过期替代作者未开源的服务端；邀请码格式校验复用原 RoomCode.cs。
//
// 隐私约定（对齐 NETWORK.md）：密码只存加盐摘要；来源 IP 只存加盐摘要、不存原文；
// 列表不下发邀请码原文；showRegion=false 时不外泄 region；房间下架/过期即删除记录。

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Campfire.Link;

namespace Campfire.Lobby;

internal sealed class RoomRecord
{
    public string Id;
    public string Token;

    /// <summary>邀请码原文，只在 /join 时下发；GET /v1/lobby 绝不包含它。</summary>
    public string Code;

    public string Title;
    public string Host;
    public string Version;
    public string Map;
    public string[] Tags;
    public int Players;
    public int MaxPlayers;
    public bool ShowRegion;
    public string Country;
    public string Region;
    public string Description;
    public bool Voice;
    public string Icon;

    public byte[] PasswordSalt;
    public byte[] PasswordHash;
    public bool Locked => PasswordHash != null;

    /// <summary>房主上报的随机 32 位 hex，本身不含个人信息，仅用于封禁与去重。</summary>
    public string Owner;

    /// <summary>来源 IP 的加盐摘要，用于按来源限流；原文不落盘。</summary>
    public string AddressDigest;

    public DateTime CreatedAtUtc;
    public DateTime LastHeartbeatUtc;
    public int Reports;
    public bool Hidden;
    public bool Removed;
}

/// <summary>临时数据，进程重启即清空；不引入数据库，符合阶段 1.5 的最小实现。</summary>
internal sealed class LobbyStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RoomRecord> _rooms = new(StringComparer.Ordinal);
    private readonly byte[] _addressSalt = RandomNumberGenerator.GetBytes(32);

    // ---------------- GET /v1/lobby ----------------

    /// <summary>返回公开视图；顺带清掉过期房间。绝不把 code / token / 摘要带出去。</summary>
    public object[] Snapshot(LobbyOptions opt)
    {
        lock (_gate)
        {
            PurgeExpiredLocked(opt);
            return _rooms.Values
                .Where(r => IsVisibleLocked(r, opt))
                .OrderBy(r => r.CreatedAtUtc)
                .Select(r => (object)new
                {
                    id = r.Id,
                    title = r.Title,
                    host = r.Host,
                    version = r.Version,
                    map = r.Map,
                    tags = r.Tags,
                    players = r.Players,
                    maxPlayers = r.MaxPlayers,
                    country = r.ShowRegion ? r.Country : "",
                    region = r.ShowRegion ? r.Region : "",
                    locked = r.Locked,
                    icon = r.Icon,
                    description = r.Description,
                    voice = r.Voice,
                    createdAt = new DateTimeOffset(r.CreatedAtUtc).ToUnixTimeMilliseconds(),
                })
                .ToArray();
        }
    }

    // ---------------- POST /v1/lobby/rooms ----------------

    public ServiceResult Publish(PublishRequest b, string remoteAddress, LobbyOptions opt)
    {
        if (!opt.Enabled)
            return ServiceResult.Fail(503, "lobby closed");

        // 客户端上报 app = "1.1.32"；低于 minAppVersion 时要求升级（LobbyError.UpdateRequired）。
        if (TryParseThreePart(opt.MinAppVersion, out Version minimum) &&
            TryParseThreePart(b.App, out Version client) && client < minimum)
            return ServiceResult.Fail(426, "update required");

        if (opt.BannedOwners.Any(o => string.Equals(o, b.Owner, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult.Fail(403, "banned");

        string rejected = FirstBlockedField(b, opt.BlockedWords);
        if (rejected != null)
            return ServiceResult.Fail(422, "blocked", rejected);

        string title = (b.Title ?? "").Trim();
        if (title.Length == 0)
            return ServiceResult.Fail(422, "blocked", "title");
        if (title.Length > 80)
            return ServiceResult.Fail(422, "too long", "title");
        if ((b.Description ?? "").Length > 500)
            return ServiceResult.Fail(422, "too long", "description");

        // 用原协议层做校验，拿规范化后的码下发，保证官方客户端 RoomCode.Find 一定能解析。
        if (string.IsNullOrWhiteSpace(b.Code) || RoomCode.Find(b.Code) is not RoomCode parsed)
            return ServiceResult.Fail(422, "invalid code", "code");
        string code = parsed.Code;

        string icon = ValidateIcon(b.Icon);
        if (icon == InvalidIconMarker)
            return ServiceResult.Fail(422, "invalid icon", "icon");

        string addressDigest = Digest(remoteAddress);
        string owner = string.IsNullOrEmpty(b.Owner) ? "" : b.Owner;
        string host = Clamp(b.Host, 100);
        string version = Clamp(b.Version, 100);
        string map = Clamp(b.Map, 100);
        string description = Clamp(b.Description, 500);
        string[] tags = (b.Tags ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToArray();
        int players = Math.Max(1, b.Players ?? 1);
        int maxPlayers = Math.Max(0, b.MaxPlayers ?? 0);
        bool showRegion = b.ShowRegion ?? false;
        bool voice = b.Voice ?? false;

        lock (_gate)
        {
            PurgeExpiredLocked(opt);

            if (_rooms.Values.Count(r => r.AddressDigest == addressDigest) >= opt.MaxRoomsPerAddress)
                return ServiceResult.Fail(429, "too many");
            if (_rooms.Count >= opt.MaxRooms)
                return ServiceResult.Fail(503, "lobby full");
            if (_rooms.Values.Any(r => r.Code == code && IsVisibleLocked(r, opt)))
                return ServiceResult.Fail(409, "already listed");

            var now = DateTime.UtcNow;
            var room = new RoomRecord
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
                Code = code,
                Title = title,
                Host = host,
                Version = version,
                Map = map,
                Tags = tags,
                Players = players,
                MaxPlayers = maxPlayers,
                ShowRegion = showRegion,
                // 本实现不接 GeoIP 数据库：只区分「本机/局域网」与「未知」，不伪造国家信息。
                Country = "",
                Region = IsLoopback(remoteAddress) ? "本机" : "未知",
                Description = description,
                Voice = voice,
                Icon = icon,
                Owner = owner,
                AddressDigest = addressDigest,
                CreatedAtUtc = now,
                LastHeartbeatUtc = now,
            };

            if (!string.IsNullOrEmpty(b.Password))
            {
                room.PasswordSalt = RandomNumberGenerator.GetBytes(16);
                room.PasswordHash = HashPassword(room.PasswordSalt, b.Password);
            }

            _rooms[room.Id] = room;
            // 响应必须含 id + token（LobbyClient.PublishAsync:224-234 缺一即抛 Unexpected）。
            return ServiceResult.Ok(new
            {
                id = room.Id,
                token = room.Token,
                title = room.Title,
                heartbeatSeconds = opt.HeartbeatSeconds,
            });
        }
    }

    // ---------------- PUT /v1/lobby/rooms/{id} ----------------

    public ServiceResult Heartbeat(string id, string token, HeartbeatRequest b, LobbyOptions opt)
    {
        lock (_gate)
        {
            if (!TryAuthorizeLocked(id, token, opt, out RoomRecord room, out ServiceResult failure))
                return failure;
            room.Players = Math.Max(1, b.Players ?? room.Players);
            room.MaxPlayers = Math.Max(0, b.MaxPlayers ?? room.MaxPlayers);
            room.Voice = b.Voice ?? room.Voice;
            room.LastHeartbeatUtc = DateTime.UtcNow;
            return ServiceResult.Ok(new { heartbeatSeconds = opt.HeartbeatSeconds });
        }
    }

    // ---------------- DELETE /v1/lobby/rooms/{id} ----------------

    public ServiceResult Close(string id, string token, LobbyOptions opt)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(id, out RoomRecord room))
                return ServiceResult.Ok(new { }); // 已经没了也算完成（LobbyClient.CloseAsync 注释）
            if (!FixedEquals(room.Token, token))
                return ServiceResult.Fail(401, "unauthorized");
            _rooms.Remove(id);
            return ServiceResult.Ok(new { });
        }
    }

    // ---------------- POST /v1/lobby/rooms/{id}/join ----------------

    public ServiceResult Join(string id, JoinRequest b, LobbyOptions opt)
    {
        lock (_gate)
        {
            PurgeExpiredLocked(opt);
            // 先判定「存在与否」，再判定下架原因，否则隐藏/下架的房间会被可见性
            // 一刀切判成 404，客户端就收不到契约里的 410 removed / reported。
            // 过期的房间已在上面的 PurgeExpiredLocked 中移除。
            if (!_rooms.TryGetValue(id, out RoomRecord room))
                return ServiceResult.Fail(404, "not found");
            if (room.Removed)
                return ServiceResult.Fail(410, "removed");
            if (room.Hidden)
                return ServiceResult.Fail(410, "reported");
            if (room.Locked)
            {
                string password = b.Password ?? "";
                if (password.Length == 0)
                    return ServiceResult.Fail(401, "password required");
                if (!CryptographicOperations.FixedTimeEquals(
                        room.PasswordHash, HashPassword(room.PasswordSalt, password)))
                    return ServiceResult.Fail(401, "wrong password");
            }
            return ServiceResult.Ok(new { code = room.Code });
        }
    }

    // ---------------- 可选端点 ----------------

    /// <summary>名字过滤；客户端在服务端不可达或返回非 ok 时自行降级，默认全放行。</summary>
    public bool IsNameAllowed(string text, LobbyOptions opt) =>
        !ContainsBlocked(text, opt.BlockedWords);

    public ServiceResult Report(string id, ReportRequest b, LobbyOptions opt)
    {
        lock (_gate)
        {
            PurgeExpiredLocked(opt);
            if (!_rooms.TryGetValue(id, out RoomRecord room))
                return ServiceResult.Fail(404, "not found");
            room.Reports++;
            if (room.Reports >= opt.ReportThreshold)
                room.Hidden = true;
            return ServiceResult.Ok(new { });
        }
    }

    /// <summary>管理员下架：之后对该 id 的 heartbeat/join 一律返回 410 removed。</summary>
    public ServiceResult AdminRemove(string id)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(id, out RoomRecord room))
                return ServiceResult.Fail(404, "not found");
            room.Removed = true;
            return ServiceResult.Ok(new { });
        }
    }

    /// <summary>后台定时清理；同时提供公开入口便于测试。</summary>
    public void PurgeExpired(LobbyOptions opt)
    {
        lock (_gate)
            PurgeExpiredLocked(opt);
    }

    public int Count
    {
        get { lock (_gate) return _rooms.Count; }
    }

    // ---------------- 内部工具 ----------------

    private void PurgeExpiredLocked(LobbyOptions opt)
    {
        DateTime cutoff = DateTime.UtcNow.AddSeconds(-opt.ExpireSeconds);
        foreach (string id in _rooms.Where(p => p.Value.LastHeartbeatUtc < cutoff).Select(p => p.Key).ToArray())
            _rooms.Remove(id);
    }

    private static bool IsVisibleLocked(RoomRecord r, LobbyOptions opt) =>
        !r.Removed && !r.Hidden && r.LastHeartbeatUtc >= DateTime.UtcNow.AddSeconds(-opt.ExpireSeconds);

    private bool TryAuthorizeLocked(string id, string token, LobbyOptions opt,
        out RoomRecord room, out ServiceResult failure)
    {
        PurgeExpiredLocked(opt);
        if (!_rooms.TryGetValue(id, out room))
        {
            failure = ServiceResult.Fail(404, "not found"); // 客户端会自动重新发布
            return false;
        }
        if (!FixedEquals(room.Token, token))
        {
            failure = ServiceResult.Fail(401, "unauthorized");
            return false;
        }
        if (room.Removed)
        {
            failure = ServiceResult.Fail(410, "removed");
            return false;
        }
        if (room.Hidden)
        {
            failure = ServiceResult.Fail(410, "reported");
            return false;
        }
        failure = null;
        return true;
    }

    private static bool FixedEquals(string expected, string actual) =>
        expected != null && actual != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static byte[] HashPassword(byte[] salt, string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password ?? "");
        byte[] buffer = new byte[salt.Length + bytes.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(bytes, 0, buffer, salt.Length, bytes.Length);
        return SHA256.HashData(buffer);
    }

    /// <summary>加盐摘要：进程内随机盐，重启后变化，不保存、不外发。</summary>
    private string Digest(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
        byte[] buffer = new byte[_addressSalt.Length + bytes.Length];
        Buffer.BlockCopy(_addressSalt, 0, buffer, 0, _addressSalt.Length);
        Buffer.BlockCopy(bytes, 0, buffer, _addressSalt.Length, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(buffer)).ToLowerInvariant();
    }

    private const string InvalidIconMarker = "\u0000INVALID_ICON";

    private static string ValidateIcon(string icon)
    {
        if (string.IsNullOrEmpty(icon))
            return null;
        // 约 256 KiB 的图片上限；base64 膨胀 4/3。
        if (icon.Length > 350_000)
            return InvalidIconMarker;
        try
        {
            byte[] decoded = Convert.FromBase64String(icon);
            return decoded.Length > 0 ? icon : null;
        }
        catch (FormatException)
        {
            return InvalidIconMarker;
        }
    }

    private static string FirstBlockedField(PublishRequest b, string[] words)
    {
        if (words.Length == 0)
            return null;
        if (ContainsBlocked(b.Title, words)) return "title";
        if (ContainsBlocked(b.Description, words)) return "description";
        if (ContainsBlocked(b.Host, words)) return "host";
        if (ContainsBlocked(b.Map, words)) return "map";
        return null;
    }

    private static bool ContainsBlocked(string text, string[] words) =>
        !string.IsNullOrEmpty(text) && words.Any(w => !string.IsNullOrEmpty(w) &&
            text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static string Clamp(string text, int max)
    {
        string value = text ?? "";
        return value.Length <= max ? value : value.Substring(0, max);
    }

    private static bool IsLoopback(string address) =>
        address == "::1" || address == "127.0.0.1" || address == "0:0:0:0:0:0:0:1" ||
        (address != null && address.StartsWith("127.", StringComparison.Ordinal));

    private static bool TryParseThreePart(string text, out Version version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] parts = text.Split('.');
        if (parts.Length != 3)
            return false;
        var numbers = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}
