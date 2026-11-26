// Campfire Linux port — 新增文件，依据 Xocio/Campfire v1.1.32 (GPL-3.0) 的
// src/Campfire/Link/Lobby/LobbyClient.cs REST 契约新建。
// 修改：实现作者未开源的 /v1/lobby* 服务端；其它在线功能（更新/公告/统计/地图）统一返回 404 静默降级。

using System.Text.Json;
using Campfire.Lobby;

// 显式把 contentRoot 钉在程序集所在目录，这样 `dotnet campfire-lobby.dll`
// 从任意工作目录启动都能读到随产物一起发布的 appsettings.json。
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

LobbyOptions options = LobbyOptions.Load(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<LobbyStore>();
builder.Services.AddHostedService<LobbyCleanupService>();

var app = builder.Build();

// ---------- 统一响应 ----------

static IResult Reply(ServiceResult r) => r.Success
    ? Results.Json(r.Body ?? new { })
    : Results.Json(new { error = r.Error, field = r.Field }, statusCode: r.Status);

static string Bearer(HttpRequest request)
{
    string header = request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? header.Substring(prefix.Length).Trim()
        : null;
}

static async Task<T> BodyAsync<T>(HttpRequest request) where T : class, new()
{
    try
    {
        return await request.ReadFromJsonAsync<T>() ?? new T();
    }
    catch (JsonException) { return new T(); }
    catch (BadHttpRequestException) { return new T(); }
}

static string Address(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// ---------- 必需端点 ----------

// GET /v1/lobby —— 列表；rooms[] 绝不包含邀请码原文。
app.MapGet("/v1/lobby", (LobbyStore store) => Results.Json(new
{
    enabled = options.Enabled,
    notice = options.Notice,
    minAppVersion = string.IsNullOrWhiteSpace(options.MinAppVersion) ? null : options.MinAppVersion,
    refreshSeconds = options.RefreshSeconds,
    tags = options.Tags,
    rooms = store.Snapshot(options),
}));

// POST /v1/lobby/rooms —— 公开房间，响应必须含 id + token。
app.MapPost("/v1/lobby/rooms", async (HttpContext context, LobbyStore store) =>
    Reply(store.Publish(await BodyAsync<PublishRequest>(context.Request), Address(context), options)));

// PUT /v1/lobby/rooms/{id} —— 心跳，仅此处校验 Bearer（连同 DELETE）。
app.MapPut("/v1/lobby/rooms/{id}", async (string id, HttpContext context, LobbyStore store) =>
    Reply(store.Heartbeat(id, Bearer(context.Request), await BodyAsync<HeartbeatRequest>(context.Request), options)));

// DELETE /v1/lobby/rooms/{id} —— 撤下房间。
app.MapDelete("/v1/lobby/rooms/{id}", (string id, HttpContext context, LobbyStore store) =>
    Reply(store.Close(id, Bearer(context.Request), options)));

// POST /v1/lobby/rooms/{id}/join —— 校验密码后才发邀请码。
app.MapPost("/v1/lobby/rooms/{id}/join", async (string id, HttpContext context, LobbyStore store) =>
    Reply(store.Join(id, await BodyAsync<JoinRequest>(context.Request), options)));

// ---------- 可选端点 ----------

// POST /v1/lobby/check —— 名字过滤；不实现时客户端本就 fail-open 全放行，这里给显式答案。
app.MapPost("/v1/lobby/check", async (HttpContext context, LobbyStore store) =>
    Results.Json(new { ok = store.IsNameAllowed((await BodyAsync<TextRequest>(context.Request)).Text, options) }));

// POST /v1/lobby/rooms/{id}/report —— 累计举报，达到阈值隐藏房间。
app.MapPost("/v1/lobby/rooms/{id}/report", async (string id, HttpContext context, LobbyStore store) =>
    Reply(store.Report(id, await BodyAsync<ReportRequest>(context.Request), options)));

// POST /v1/lobby/rooms/{id}/remove —— 管理员下架；AdminToken 留空时整个端点伪装成 404。
app.MapPost("/v1/lobby/rooms/{id}/remove", (string id, HttpContext context, LobbyStore store) =>
{
    string configured = options.AdminToken;
    string supplied = context.Request.Headers["X-Admin-Token"].ToString();
    if (string.IsNullOrEmpty(configured) || !string.Equals(configured, supplied, StringComparison.Ordinal))
        return Results.Json(new { error = "not found" }, statusCode: 404);
    return Reply(store.AdminRemove(id));
});

// 服务端运行信息，便于本机 curl 验证（不属于官方契约）。
app.MapGet("/v1/lobby/_stats", (LobbyStore store) => Results.Json(new
{
    rooms = store.Count,
    heartbeatSeconds = options.HeartbeatSeconds,
    expireSeconds = options.ExpireSeconds,
    maxRooms = options.MaxRooms,
}));

// 其余在线功能（/v1/manifest、公告、统计、地图、CurseForge…）一律 404，让客户端静默降级。
app.MapFallback(() => Results.Json(new { error = "not found" }, statusCode: 404));

app.Run();
