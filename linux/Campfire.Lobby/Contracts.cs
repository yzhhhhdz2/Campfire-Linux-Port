// Campfire Linux port — 新增文件，依据 Xocio/Campfire v1.1.32 (GPL-3.0) 的
// src/Campfire/Link/Lobby/LobbyClient.cs REST 契约新建。
// 修改：把客户端真正会发出的字段逐个定义成 DTO，字段名用 [JsonPropertyName] 锁死为 camelCase。

using System.Text.Json.Serialization;

namespace Campfire.Lobby;

/// <summary>POST /v1/lobby/rooms —— 房主公开房间时提交的字段（LobbyClient.PublishAsync:203-222）。</summary>
internal sealed class PublishRequest
{
    [JsonPropertyName("owner")] public string Owner { get; set; }
    [JsonPropertyName("app")] public string App { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; }
    [JsonPropertyName("host")] public string Host { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("map")] public string Map { get; set; }
    [JsonPropertyName("tags")] public string[] Tags { get; set; }
    [JsonPropertyName("players")] public int? Players { get; set; }
    [JsonPropertyName("maxPlayers")] public int? MaxPlayers { get; set; }
    [JsonPropertyName("showRegion")] public bool? ShowRegion { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("voice")] public bool? Voice { get; set; }
    [JsonPropertyName("password")] public string Password { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
}

/// <summary>PUT /v1/lobby/rooms/{id} —— 心跳（LobbyClient.HeartbeatAsync:241）。</summary>
internal sealed class HeartbeatRequest
{
    [JsonPropertyName("players")] public int? Players { get; set; }
    [JsonPropertyName("maxPlayers")] public int? MaxPlayers { get; set; }
    [JsonPropertyName("voice")] public bool? Voice { get; set; }
}

/// <summary>POST /v1/lobby/rooms/{id}/join —— 换取邀请码（LobbyClient.GetCodeAsync:255-257）。</summary>
internal sealed class JoinRequest
{
    [JsonPropertyName("password")] public string Password { get; set; }
}

/// <summary>POST /v1/lobby/check 与 /report 的请求体。</summary>
internal sealed class TextRequest
{
    [JsonPropertyName("text")] public string Text { get; set; }
}

internal sealed class ReportRequest
{
    [JsonPropertyName("reason")] public string Reason { get; set; }
}

/// <summary>所有端点统一的结果包装：成功给 JSON body，失败给 error/field + 非 2xx 状态码。</summary>
internal sealed class ServiceResult
{
    public int Status { get; private init; }
    public string Error { get; private init; }
    public string Field { get; private init; }
    public object Body { get; private init; }

    public bool Success => Status >= 200 && Status < 300;

    public static ServiceResult Ok(object body) => new() { Status = 200, Body = body };

    public static ServiceResult Fail(int status, string error, string field = null) =>
        new() { Status = status, Error = error, Field = field };
}
