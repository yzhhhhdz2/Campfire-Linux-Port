# 自建联机大厅服务端（阶段 1.5）

基于 `Xocio/Campfire v1.1.32`（GPL-3.0）的客户端契约新建的 **ASP.NET Core Minimal API / .NET 8** 服务端。
原作者只开源了客户端 `src/Campfire/Link/Lobby/LobbyClient.cs`，`api.mccampfire.top` 的服务端源码没有公开，
所以「保留公开房间列表」这一需求只能自己实现一个协议兼容的服务端。

> **再次提醒：大厅 ≠ 联机。** 邀请码 `U/XXXX-XXXX-XXXX-XXXX` 在客户端本地换算成 EasyTier 的
> `network_name` / `network_secret`，靠公共节点发现 + 点对点直连，游戏数据**完全不经过本服务**。
> 本服务只是「广场上的告示板」：存房间目录、在有人想加入时下发邀请码。没有它，照样能用邀请码联机。

## 端点契约

全部来自 `LobbyClient.cs`，JSON 严格 camelCase，所有计数与 `createdAt` 必须是 **JSON number**（客户端手写 `Json.cs` 只把数字解析成 `double`）。

| 端点 | 方法 | 必需 | 请求 → 响应 |
|---|---|---|---|
| `/v1/lobby` | GET | ✅ | → `enabled,notice,minAppVersion,refreshSeconds,tags,rooms[]`；`rooms[]` 字段 `id,title,host,version,map,tags,players,maxPlayers,country,region,locked,icon(base64),description,voice,createdAt(Unix 毫秒)`。**列表绝不含邀请码原文** |
| `/v1/lobby/rooms` | POST | ✅ | `owner,app,code,title,host,version,map,tags,players,maxPlayers,showRegion,description,voice[,password,icon]` → **必须含 `id` + `token`**，可含 `title,heartbeatSeconds` |
| `/v1/lobby/rooms/{id}` | PUT | ✅ | `Authorization: Bearer <token>`；`players,maxPlayers,voice` → `heartbeatSeconds` |
| `/v1/lobby/rooms/{id}` | DELETE | 建议 | `Authorization: Bearer <token>` → `{}`；房间已不存在也算成功 |
| `/v1/lobby/rooms/{id}/join` | POST | ✅ | `{"password":...}`（有密码时）→ `{"code":"U/..."}`，必须是 `RoomCode.Find` 能解析的码 |
| `/v1/lobby/check` | POST | 可选 | `{"text":名字}` → `{"ok":bool}`；违禁词表为空时一律 true |
| `/v1/lobby/rooms/{id}/report` | POST | 可选 | `{"reason":"content"|"unreachable"}`；累计达 `ReportThreshold` 隐藏房间 |
| `/v1/lobby/rooms/{id}/remove` | POST | 可选 | `X-Admin-Token: <AdminToken>`；管理端点，`AdminToken` 留空时整个端点伪装成 404 |
| `/v1/lobby/_stats` | GET | 本分支新增 | 运行信息，便于本机验收；不属于官方契约 |
| 其它路径（`/v1/manifest` 等） | * | — | 统一 **404 JSON**，让更新/公告/统计/在线地图客户端静默降级 |

**鉴权**：只有 `Bearer <token>`，且**仅 PUT / DELETE 校验**；token 是 POST 下发的不透明随机串，无签名/时间戳。
`owner`(32 hex) 与 `app`("1.1.32") 只是普通字段，用于封禁判断与版本门禁。

**邀请码校验**：服务端通过 `<Compile Include="../../src/Campfire/Link/RoomCode.cs">` **链接原协议文件**，
用 `RoomCode.Find` 校验并保存规范化后的 `Code`。这样下发的码 100% 能被官方客户端解析，也不会出现协议漂移。

### 错误码映射（对齐 `LobbyClient.Failure`）

| 状态 | `error` | 触发条件 | 客户端 `LobbyError` |
|---|---|---|---|
| 401 | `password required` / `wrong password` | 密码房间未给 / 给错密码 | `WrongPassword` |
| 401 | `unauthorized` | PUT/DELETE 的 token 不对 | `Unexpected` |
| 403 | `banned` | `owner` 命中 `BannedOwners` | `Banned` |
| 404 | `not found` | 房间不存在 / 已过期 / 已撤下 / 未知端点 | `NotFound` |
| 409 | `already listed` | 同一邀请码已公开 | `AlreadyListed` |
| 410 | `removed` | 管理员下架 | `Removed` |
| 410 | `reported` | 举报达阈值隐藏 | `Reported` |
| 422 | `blocked` + `field` | 标题/描述等命中违禁词、缺标题、非法邀请码/图标 | `Blocked` |
| 426 | `update required` | `app` 低于 `MinAppVersion` | `UpdateRequired` |
| 429 | `too many` | 同一来源同时公开的房间数超 `MaxRoomsPerAddress` | `TooMany` |
| 503 | `lobby full` | 全局房间数达 `MaxRooms` | `Full` |
| 503 | `lobby closed` | `Enabled=false` | `Closed` |

> 错误体统一 `{"error":"...","field":...}`，**不要**加 `campfire:` 前缀（那是房间内 TCP 协议的回复常量）。

### 隐私与安全（`NETWORK.md` 约定）

- 密码**只存加盐 SHA-256 摘要**，比对用 `CryptographicOperations.FixedTimeEquals`；服务端看不到原文。
- 来源 IP **只存进程内随机盐的摘要**，不存原文、不落盘，仅用于按来源限流。
- 列表不下发邀请码原文；`showRegion=false` 时 `country`/`region` 一律为空。
- 本实现**不接 GeoIP 数据库**：`showRegion=true` 只区分「本机」与「未知」，不伪造国家信息。
- 房间是内存字典，心跳缺席超过 `ExpireSeconds` 即删除；进程重启即清空。后台每 30 秒清理一次。

## 启动与验证

```bash
cd ~/campfire-src/linux
bash build.sh                 # 同时编译 Campfire.Cli 与 Campfire.Lobby
./run-lobby.sh                # 默认 http://127.0.0.1:5080，只监听本机
# 另开一个终端：
./verify-lobby.sh             # 42 项 curl 断言，末行 LOBBY VERIFY OK
```

启动服务端时可用环境变量覆盖配置：

```bash
Lobby__AdminToken=dev-test-token Lobby__ReportThreshold=2 Lobby__MinAppVersion=1.1.32 ./run-lobby.sh
ADMIN_TOKEN=dev-test-token ./verify-lobby.sh      # 同时启用 410 removed 用例
```

接上 CLI（三种写法等价，只用其中一个）：

```bash
campfire --service-url http://127.0.0.1:5080 lobby list
campfire config set service.url http://127.0.0.1:5080 && campfire lobby list
export CAMPFIRE_SERVICE_URL=http://127.0.0.1:5080 && campfire lobby list

# 公开一个已有邀请码（来自另一个正在 host 的客户端），Ctrl+C 撤下
campfire --service-url http://127.0.0.1:5080 lobby publish \
    --code U/XXXX-XXXX-XXXX-XXXX --title '我的房间' --password 1234
# 从大厅选房间换邀请码，再走标准 join
campfire --service-url http://127.0.0.1:5080 lobby join <房间ID> --password 1234
campfire join --code U/XXXX-XXXX-XXXX-XXXX
```

`--service-url` 只接受 `https` 或**指向本机的 http**（对齐官方 `OnlineService.ReadServiceUrl` 的校验）。
要给局域网里的别人用，得改监听地址并处理 HTTPS（非 loopback 只允许 https）。

## 配置（`appsettings.json` 的 `Lobby` 段）

| 键 | 默认 | 说明 |
|---|---|---|
| `Enabled` | true | false 时列表仍可看、发布返回 503 |
| `Notice` | 中文提示 | 列表上方公告 |
| `AdminToken` | 空 | 留空则管理端点不可用 |
| `MinAppVersion` | 空 | `"1.1.32"` 三段；低版本 POST 返回 426，仍可浏览 |
| `RefreshSeconds` | 30 | 客户端钳到 ≥5 |
| `HeartbeatSeconds` | 60 | 客户端钳到 30..3600 |
| `ExpireSeconds` | 180 | 心跳缺席多久算过期；至少为心跳间隔 2 倍 |
| `MaxRooms` / `MaxRoomsPerAddress` | 200 / 5 | 触发 503 / 429 |
| `ReportThreshold` | 3 | 举报隐藏阈值 |
| `Tags` / `BannedOwners` / `BlockedWords` | 见文件 | 标签、封禁列表、违禁词 |

`contentRoot` 已在 `Program.cs` 里钉到程序集目录，所以从任意工作目录执行 `dotnet campfire-lobby.dll` 都能读到随产物发布的 `appsettings.json`。

## 边界（务必记住）

- 官方 Windows `Campfire.exe` 编译时把 `ServiceUrl` 烧成了 `api.mccampfire.top`，**看不到本服务的房间**；反过来也一样。想让别人逛你的大厅，对方必须用指向本服务的构建。
- 这**不影响邀请码互通**：Linux CLI 与官方 Windows 客户端照样能互相加入，因为那条链路根本不调用 `OnlineService`。
- 不要把这个服务指向官方地址，也不要把自己的客户端 `--service-url` 填成官方地址。作者明确把「源码构建不连服务」写成设计意图；且服务端能通过 UA（`Campfire-Linux/1.1.32` vs `Campfire/1.1.32`）、`owner`、`app` 识别第三方构建。

## 验证结果（2026-11-15，Windows 开发机实跑）

```
两个工程 dotnet build -c Release   →  0 警告 0 错误
./verify-lobby.sh                  →  42 项全过，0 失败 0 跳过，LOBBY VERIFY OK
campfire selftest                  →  11 项全过（回归）
campfire lobby list / publish --once / join  →  全部通过
配置触发的 429 / 503               →  临时实例验证通过（5081/5082）
```

`403 banned` 分支由 `BannedOwners` 配置触发，代码存在但未单独跑用例。

## GPL-3.0 合规

- 本目录所有新文件头部都标注了「基于 Xocio/Campfire v1.1.32 (GPL-3.0)」及修改内容；链接进来逐字未改的 `RoomCode.cs` 不加声明。
- 分发二进制须同时提供本 fork 的完整对应源码，并保留 `../../LICENSE` 与 `../../THIRD-PARTY-NOTICES.md`。
- 本服务端不引入新的第三方 NuGet 依赖（只用 .NET 8 共享框架），EasyTier / dnlib / ASM 的许可义务仍按原声明执行。
