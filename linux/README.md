<p align="center">
  <b>简体中文</b> | <a href="README.zh-Hant.md">繁體中文</a> | <a href="README.en.md">English</a>
</p>

# Campfire Linux 移植

基于 `Xocio/Campfire v1.1.32`，GPL-3.0。原 Windows 工程 `../src/Campfire/` **全程零改动**，只作为源码来源与对照基线；`linux/` 全是新增文件，删掉即可完全回退。

> 这是一个社区移植分支，不是官方版本；不连接原作者的大厅服务。

## 当前进度：阶段 1 / 阶段 2 / 26.3 端到端 全部完成 ✅

| 目录 | 状态 |
|---|---|
| `Campfire.Cli/` | ✅ 完整 `Link/` 层 + `LinkSession` 编排层 + Linux 适配层 + CLI 会话层 + 大厅客户端；`selftest` **11 项全过** |
| `Campfire.Lobby/` | ✅ ASP.NET Core Minimal API 自建大厅，`verify-lobby.sh` **42 项全过**（详见 `Campfire.Lobby/README.md`） |
| `Campfire.Avalonia/` | ✅ **两批全部交付**：Avalonia 11 编译 0 警告 0 错误；4 Tab + 完整动态主题（OKLCH / 9 种子色 / 6 风格 / 400ms 过渡）+ 地图只读扫描 / 工具真功能 + 13 项界面动效 + **简 / 繁 / EN 三语界面** |

### 阶段 1 验收结果（2026-11-18，Bazzite VM × 官方 Windows `Campfire.exe` v1.1.32 实测）

| 方向 | 房主 | 加入方 | 邀请码 | 结果 |
|---|---|---|---|---|
| A | Linux CLI（`--skip-probe`） | 官方 Windows 客户端 | `U/K3LB-EP2X-V5HG-TGM9` | ✅ `阶段=Ready 玩家=2 直连=1`；Windows 端直连·TCP 0.7 ms、NAT4 |
| B | 官方 Windows 客户端 + `windows-test/` 假世界桩（游戏端口 25565） | Linux CLI（`--skip-probe`） | `U/LBFC-QVX9-GYL7-EZH3` | ✅ `阶段=Ready 玩家=2`；Windows 端直连 1 · 中转 0、0.5 ms |

两个方向都通 → **阶段 1 硬标准达成**。免剪贴板脚本：`step2.sh`（装组件）、`step3.sh`（回环自检）、`step4.sh`（双向互通）。

> **边界，别当成"能联机打游戏了"**：两端都没装真实 MC、全程 `--skip-probe`，只验「邀请码 → RoomCode → EasyTier 组网 → 直连 TCP → 房间协议 → 玩家列表」。这是阶段 1 当时的边界：实际进游戏世界、模组同步、语音、官方大厅均未验证（后两项本就裁剪）；**2026-11-25 已用真实 MC 26.3 + 自动开放局域网补齐「进游戏世界」这一项**，详见「Minecraft 26.3 端到端实测」一节，其余仍属裁剪。玩家列表里 `Campfire 1.0.0` 只是 Linux CLI 程序集版本显示，房间协议无版本校验；监控里偶发 `直连=0 / 节点=-1` 是 EasyTier RPC 快照滞后，以对端显示为准。

### 工程结构

| 路径 | 作用 |
|---|---|
| `Campfire.Cli/Campfire.Cli.csproj` | `net8.0` 控制台工程。用**共享源码链接**把 `../../src/Campfire/Link/**/*.cs` 逐字编进来（禁止副本与编辑，防协议漂移）；剔除 10 个 Windows 专属文件 |
| `Campfire.Cli/Link/LinkSession.cs` | **唯一物理复制并改写的原文件**（1743 行编排层）。仅新增 `SkipProbe` 开关（4 处探测短路），其余逐字不变 |
| `Campfire.Cli/Cli/` | 命令层：`SelfTest`、`VersionCommand`、`Doctor`、`ConfigCommand`、`EasyTierCommand`、`LobbyCommand`、`SessionCommands`、`SessionRuntime`（消息泵 / 状态快照 / `CliHost`） |
| `Campfire.Cli/Linux/` | **真正的 Linux 适配层**：`EasyTierPackage`（Linux 发行包 + chmod）、`ChildProcessJob`（退出钩子替代 Job Object）、`GameAccount`、`LocalWorldFinder`（`/proc`）、`LauncherProcess`、`AppSettings`（XDG）、`LinuxConfig` |
| `Campfire.Cli/Shims/` | 被裁剪功能的**安全空实现**：`Text`、`Mods`、`Worlds`、`OnlineService`、`Telemetry`、`MinecraftStubs`（JavaAgent / AgentState / LanOpener） |
| `Campfire.Lobby/` | 自建大厅服务端：`Program.cs`（Minimal API 端点）、`LobbyStore.cs`（内存字典 + 心跳过期 + 密码/IP 摘要）、`LobbyOptions.cs`、`Contracts.cs`、`LobbyCleanupService.cs`、`appsettings.json` |
| `build.sh` / `run-lobby.sh` / `verify-lobby.sh` | 一键构建两个工程 / 启动大厅 / 42 项契约验收 |
| `fetch-easytier.sh` | EasyTier 2.6.4 Linux 组件断点续传下载 |

**关键取舍**：原计划想「大段删掉 700 行裁剪功能」，实际改成 **给一套最小 shim 让 `LinkSession.cs` 几乎逐字不改地编译**。好处是协议常量、房间编排、EasyTier 调用链完全不动，最大程度避免与官方 Windows 客户端协议漂移；被裁剪的功能由 shim 恒返回「未生效」的安全值静默禁用。服务端同样链接原 `RoomCode.cs` 做邀请码校验，防止下发客户端解析不了的码。

被剔除的 10 个原文件：`LinkSession.cs`、`Diagnostics.cs`、`Minecraft/{GameAccount,LocalWorldFinder,JavaAgent,LanOpener,PlayerHeads,GameMappings}.cs`、`Runtime/{EasyTierPackage,ChildProcessJob}.cs`。其中 4 个由 `Linux/` 同名类替代，其余按计划删除或由 shim 承接。

## 搬到 Linux 机器上（部署）

### 最容易踩的坑：只拷 `linux/` 一定编译失败

两个 `.csproj` 都用 `$(MSBuildThisFileDirectory)../../src/Campfire/` **共享源码链接**复用原协议层（协议文件禁止复制、禁止改写，防止与官方客户端协议漂移）。所以 Linux 上必须保持这个同级结构：

```
~/campfire-linux/                      ← 包根，路径别带空格 / 中文
├── bootstrap.sh
├── LICENSE  THIRD-PARTY-NOTICES.md
├── src/Campfire/
│   ├── Hex.cs
│   └── Link/**/*.cs                   ← 41 个原协议层文件，逐字未改
└── linux/
    ├── Campfire.Cli/
    ├── Campfire.Lobby/
    ├── build.sh / run-lobby.sh / verify-lobby.sh / fetch-easytier.sh
    └── README.md
```

> ⚠️ 你现在 Windows 上的目录叫 `camp fire linux移植计划`，**既有空格又有中文**。脚本虽然都加了引号，但 Linux 上强烈建议一律解压到 `~/campfire-linux`，少踩一堆路径坑。

### 方案 A：一个包全带走（推荐，最省事）

在本机 Git Bash 里生成自包含源码包：

```bash
cd "/c/Users/yzhhh/Desktop/camp fire linux移植计划/Campfire-1.1.32/Campfire-1.1.32/linux"
bash pack-for-linux.sh
```

产出 `campfire-linux.tar.gz` + `.sha256`。包里已含原协议层、GPL 合规文件、全部新代码与脚本；`.sh` 统一转成 LF 并加了可执行位（CRLF 的脚本在 Linux 会报 `bad interpreter`）。改过代码后重跑一次即可重新打包。

**把包弄到 Linux**（挑一种）：

| 方式 | 怎么做 |
|---|---|
| WSL2（最省，连打包都不用） | `cp -r "/mnt/c/Users/yzhhh/Desktop/camp fire linux移植计划/Campfire-1.1.32/Campfire-1.1.32" ~/campfire-linux`。WSL 能直接读 C 盘；**但 Windows 文件系统上编译很慢，务必先 cp 到 `~/` 下的 ext4** |
| U 盘 / 虚拟机共享文件夹 | 把 `campfire-linux.tar.gz` 拷过去，完全不依赖网络，学校网慢时首选 |
| 同一局域网 `scp` | Linux 上先 `sudo apt install -y openssh-server`，Windows 侧 `scp campfire-linux.tar.gz 用户名@Linux的IP:~/` |

### 方案 B：只传 `linux/`（传输量最小，约 200 KB）

Linux 机器能上网时，在那边重新下官方源码，只把我们的 `linux/` 目录拷过去：

```bash
mkdir -p ~/campfire-linux && cd ~/campfire-linux
wget -c https://github.com/Xocio/Campfire/archive/refs/tags/v1.1.32.tar.gz -O src.tar.gz
tar -xzf src.tar.gz --strip-components=1        # 得到 src/Campfire/... 等
# 再把 Windows 上的 linux/ 整个目录拷进来（U 盘 / scp）
cp -r /path/to/linux ./linux
```

学校网慢就用迅雷在 Windows 上下 `https://github.com/Xocio/Campfire/archive/refs/tags/v1.1.32.zip`，解压后拷过去，内容完全一样。

### 方案 C：VMware 虚拟机拖不进去时（Bazzite / 不可变发行版）

**为什么会「毫无反应」**：VMware 的拖放、剪贴板共享、共享文件夹都不是虚拟机自带功能，要靠客户机里 `open-vm-tools-desktop` 的 `vmware-user` 进程。Bazzite 基于 Fedora Atomic（Kinoite / Silverblue），`/usr` 只读、面向裸机游戏设备，默认不装这套工具；而且它默认跑 **Wayland**，而 VMware 拖放走的是 X11 Xdnd 协议，Wayland 下本来就是坏的（open-vm-tools 官方 issue #792 正在跟踪，其中就包含基于 Bazzite 的镜像）。所以文件**根本没进虚拟机**，不是包坏了。

另一点：`.tar.gz` **不是可执行程序**，双击本来就不会「弹出安装界面」，要用终端 `tar -xzf` 解开；Bazzite 若没装归档管理器，双击确实什么都不发生。另外 VMware 拖放对**整个文件夹**支持很差，只适合拖单个文件。

**第 0 步：先确认文件到底有没有进去**（Bazzite 里开终端）：

```bash
ls -la ~/Downloads ~/Desktop 2>/dev/null
find ~ -maxdepth 3 -name 'campfire-linux*' 2>/dev/null
```

找得到 → 直接跳到下面「解压 + 初始化」，不用再传。找不到 → 用下面的 HTTP 办法。

**不依赖 VMware Tools 的传法：临时 HTTP 服务**。已写好 `linux/serve-for-vm.py`（只读、不改系统、Ctrl+C 即消失，本机实测下载后字节完全一致）：

Windows 主机上执行（Git Bash 或终端）：

```bash
python "C:/Users/yzhhh/Desktop/camp fire linux移植计划/Campfire-1.1.32/Campfire-1.1.32/linux/serve-for-vm.py"
```

脚本会打印本机所有 IPv4 和完整下载命令（**一律以它打印的为准**）。

> ⚠️ **别照抄聊天或旧文档里的示例 IP**。本机 WLAN 实际是 `172.16.202.127`（校园网 JunCheng-Teacher 4，DHCP 会变）；早期写过的 `192.168.1.5` 只是示例，这台机器上根本不存在，照抄必然卡 21 秒然后 `curl: (7) Failed to connect`。

防火墙：WLAN 目前是「公用网络」，已有 `python.exe` 入站放行规则；首次弹窗勾「允许访问」，然后**让这个窗口一直开着**（关掉服务就没了）。

Bazzite 虚拟机里按顺序做：

```bash
cd ~

# 1) 先看自己有没有 IP，以及 NAT 模式下的主机网关地址
ip -4 addr show
ip route | awk '/default/{print $3}'

# 2) 填主机地址：NAT 用上面 ip route 的输出；桥接用主机 WLAN 地址
HOST_IP=172.16.202.127

# 3) 下载 + 校验（看到 campfire-linux.tar.gz: OK 才算成功）
curl -f -O --max-time 30 http://$HOST_IP:8000/campfire-linux.tar.gz
curl -f -O --max-time 30 http://$HOST_IP:8000/campfire-linux.tar.gz.sha256
sha256sum -c campfire-linux.tar.gz.sha256
```

懒得填 IP，就一键把常见地址全试一遍：

```bash
for ip in $(ip route | awk '/default/{print $3}') 172.16.202.127 192.168.1.1 192.168.137.1 192.168.220.1; do
  if curl -fsS --max-time 5 -o /dev/null "http://$ip:8000/"; then echo "通了: $ip"; echo "$ip" > ~/cf-host-ip; break; fi
done
HOST_IP=$(cat ~/cf-host-ip)
```

连不上按顺序查：

1. 虚拟机里 `ping -c 3 $HOST_IP`，不通就是网络层问题，往下走。
2. 主机自测 `curl -s -o /dev/null -w "%{http_code}" http://172.16.202.127:8000/`，应返回 `200`。不是 200 说明服务没起，重跑 `serve-for-vm.py`。
3. 管理员 PowerShell 里放行端口（自测 200 但虚拟机超时，多半是它）：
   ```powershell
   New-NetFirewallRule -DisplayName "Campfire HTTP 8000" -Direction Inbound -Protocol TCP -LocalPort 8000 -Action Allow -Profile Any
   ```
4. **本机 `VMnet1` / `VMnet8` 虚拟网卡当前是 Down / Not Present** —— NAT 和「仅主机」模式可能压根没网。VMware 菜单「编辑 → 虚拟网络编辑器 → 还原默认设置」（要管理员）重建虚拟网卡；或把虚拟机网络改成**桥接模式**并勾「复制物理网络连接状态」。
5. 校园 Wi-Fi 若开了 AP 客户端隔离，桥接的虚拟机也 ping 不通主机 → 换手机热点，或直接用 U 盘。
6. 还不行就换端口：`CF_PORT=8001 python ".../linux/serve-for-vm.py"`，虚拟机里把 `:8000` 改成 `:8001`。

> 兜底：不想折腾网络就用 U 盘（VMware 菜单「虚拟机 → 可移动设备 → 连接 USB」，Bazzite 会挂到 `/run/media/$USER/`）。共享文件夹要 `rpm-ostree install open-vm-tools` 改基础层并重启，不推荐。
>
> 校验文件缺失时不要慌：那只是上一条 `curl` 失败没下到本地，不是包坏了。先在浏览器打开 `http://<主机IP>:8000/` 看目录里有没有两个文件。

**Bazzite 上跑 bootstrap 的注意事项**：

| 事项 | 说明 |
|---|---|
| 别用 `dnf install` | `/usr` 只读，装系统包要靠 `rpm-ostree`（需重启）。本方案**完全不碰系统层** |
| .NET 8 SDK | 用 `bash bootstrap.sh --install-sdk`，装到 `~/.dotnet`（`$HOME` 可写），不需要 sudo |
| 必备命令 | `tar` / `curl` / `sha256sum` / `bash` 都在基础镜像里，无需额外安装 |
| 数据目录 | 默认 `~/.local/share/campfire-linux/`，在 `$HOME` 下，可写 |
| 执行方式 | 显式写 `bash bootstrap.sh`；Wayland / X11 无所谓，全程在终端里做 |
| 若 .NET 报缺 `libicu` / `libssl` | 退路是用 Bazzite 自带的 Distrobox：`distrobox create -n cf -i ubuntu:24.04 && distrobox enter cf`，在容器里跑 `bootstrap.sh` |

### 在 Linux 上解压 + 初始化

```bash
cd ~
sha256sum -c campfire-linux.tar.gz.sha256     # 可选：确认传输没损坏
tar -xzf campfire-linux.tar.gz                # 得到 ~/campfire-linux/
cd campfire-linux
bash bootstrap.sh                            # 检查 SDK → 修权限 → 构建两个工程 → 11 项自检 + doctor
```

没装 .NET 8 SDK 时 `bootstrap.sh` 会打印三条安装命令并停下；也可以一步到位（装到 `~/.dotnet`，不需要 sudo，支持断点续传）：

```bash
bash bootstrap.sh --install-sdk
```

成功的标志：末行 `SELFTEST OK`，`doctor` 在 Linux 上除「EasyTier 组件未安装」外全绿。脚本还会打印一个 `alias campfire=...`，建议照抄执行，后面命令就不用写一长串路径。

### 手动构建（不跑 bootstrap 时）

```bash
cd ~/campfire-linux/linux/Campfire.Cli
dotnet build -c Release
dotnet run -c Release --no-build -- selftest
dotnet run -c Release --no-build -- doctor
```

`build.sh` 同时编译 `Campfire.Cli` 与 `Campfire.Lobby`（都用 .NET 8 共享框架，无额外 NuGet 依赖）。

### 还要单独下 EasyTier（约 25 MB）

**源码包里不含 EasyTier 二进制**（LGPL-3.0，按需下载，且必须与官方 Windows 客户端同为 2.6.4）。到 Linux 上后执行 `bash linux/fetch-easytier.sh`，详见下文「EasyTier 2.6.4 Linux 组件」一节。学校网慢可用迅雷在 Windows 下好 zip 再拷过去，用 `campfire easytier install --local <zip>` 安装。

## CLI 命令

```bash
campfire --help
campfire selftest                       # 11 项断言，末行 SELFTEST OK
campfire version
campfire doctor                         # 平台 / 数据目录 / 组件 / 服务地址 / 编译内容
campfire config path|list|get <键>|set <键> <值>|unset <键>
campfire easytier status|install [--local <zip>]|check|uninstall
campfire lobby status|list|publish|join  # 自建大厅，需 --service-url
campfire code                           # 生成一个合法邀请码
campfire code --check <邀请码>          # 校验并展开 network_name / network_secret

# 联机会话（须在 Linux 上）
campfire host --port 43210 --player 小明 --skip-probe
campfire join --code U/XXXX-XXXX-XXXX-XXXX --player 小红 --skip-probe
campfire status | peers | logs --lines 100 | stop
```

CLI 的 `host` 需要 `--port`：命令行模式不带游戏内注入，需先在 Minecraft 里点「对局域网开放」，再把端口传给程序。**GUI 的正式联机入口**已恢复 Java Agent 自动开放（见后文「自动开放局域网世界」章节），建房页勾上后不用手填端口。
`join` 成功后会打印本地端口，Minecraft 多人游戏里连 `127.0.0.1:<本地端口>`。

可用参数：`--player`、`--local-port`、`--node`（自定义节点）、`--hide-ip`、`--no-relay`、`--share-lan`、`--timeout 30|60|120`、`--skip-probe`。

全局选项（可放在命令前后）：

| 选项 | 作用 |
|---|---|
| `--data-dir <目录>` | 覆盖 XDG 数据目录，便于同机跑两个实例做回环测试 |
| `--service-url <地址>` | 自建大厅地址；只接受 https 或指向本机的 http（对齐官方校验规则） |

> `--skip-probe` 跳过 `MinecraftProbe` 的 4 处世界探测，让没有真实 MC 世界时也能做回环联机自检。正式联机**不要**加，否则不会校验端口确实是游戏服务器。

### 会话模型（重要）

`host` / `join` 是**前台长驻进程**，保持运行房间才存在。CLI 是「一个命令一个进程」，所以长驻进程周期性把状态写到 `<数据目录>/session.json`，`status` / `peers` 读它，`stop` 写 `stop.requested`、前台进程在 1 秒内检测到并释放房间。

因此 **`host`/`join` 与查询命令必须使用同一个 `--data-dir`**。回环测试时开两个终端、各用不同的 `--data-dir`，但查询时要带上对应那一个。

`logs` 读 `<数据目录>/Link/link.log`（原 `LinkLog` 路径，超 2 MB 自动回滚），里面也有 easytier-core 最近的输出。

数据目录默认 `~/.local/share/campfire-linux/`（遵循 `XDG_DATA_HOME`），配置存 `settings.ini`。`host`/`join` 启动时会把已裁剪项显式关掉并写入配置：`AutoOpenLan`、`BackupWorld`、`Voice`、`ShareMods`、`ServeMods`、`AutoCopyCode`、`DetectClipboard`。

## 自建联机大厅（阶段 1.5）

作者只开源了 REST 客户端 `LobbyClient.cs`，`api.mccampfire.top` 的服务端没公开，所以「保留公开房间列表」只能自建。服务端契约、错误码表、配置与隐私约定全部写在 **`Campfire.Lobby/README.md`**。

```bash
cd ~/campfire-linux/linux
./run-lobby.sh                 # 默认 http://127.0.0.1:5080，只监听本机
# 另开终端：
./verify-lobby.sh              # 42 项 curl 断言 → LOBBY VERIFY OK

campfire --service-url http://127.0.0.1:5080 lobby list
campfire --service-url http://127.0.0.1:5080 lobby publish \
    --code U/XXXX-XXXX-XXXX-XXXX --title '我的房间' [--password 1234] [--once]
campfire --service-url http://127.0.0.1:5080 lobby join <房间ID> [--password 1234]
```

`lobby publish` 默认前台长驻并按服务端给的心跳间隔续约，Ctrl+C 撤下；`--once` 发布后立即撤下，用于自检。它**不创建联机房间**，邀请码必须来自另一个正在 `host` 的客户端。

**三条边界**（最容易误解）：

1. **自建大厅与官方大厅互不可见**：官方 exe 编译时把 `ServiceUrl` 烧进程序集，指向 `api.mccampfire.top`；只有指向本服务的构建能看到这里的房间。
2. **不影响邀请码互通**：邀请码走 EasyTier 公共节点 + 点对点，整条链路不调用 `OnlineService`，Linux CLI ⇄ 官方 Windows 客户端照常互进。
3. **不要连官方服务**：源码构建不连在线服务是作者明确的设计意图；自建服务端对 `/v1/manifest` 等一律返回 404 静默降级。

## EasyTier 2.6.4 Linux 组件

版本**必须锁 2.6.4**，与官方 Windows 客户端一致，否则虚拟网络协议不兼容。已核对 GitHub Release v2.6.4 的实际资产名：

| 架构 | 资产名 | 大小 |
|---|---|---|
| x86_64 | `easytier-linux-x86_64-v2.6.4.zip` | 25.5 MB |
| aarch64 | `easytier-linux-aarch64-v2.6.4.zip` | 20.5 MB |

```bash
# 断点续传下载（多个源自动切换，含国内镜像）
bash fetch-easytier.sh
# 然后装好
campfire easytier install --local ./easytier-linux-x86_64-v2.6.4.zip
campfire easytier check
```

也可以让程序自己下载：`campfire easytier install`。学校网慢时推荐先用下载工具（迅雷/aria2）下好 zip，再用 `--local` 或环境变量 `CAMPFIRE_EASYTIER_ZIP` 指定。

> **校验和说明（重要）**：官方 v2.6.4 Release **没有提供 Linux 资产的校验和文件**（Windows 版源码里硬编码了三个文件的 SHA-256，Linux 版没有对应数据）。本分支**不编造哈希**：`InstallAsync` 解压后会自动 `chmod +x`，若你在安装目录放了 `checksums.sha256`（每行 `<hex>  文件名`）就逐项比对，否则记一条警告。需要严格校验时请自行 `sha256sum` 核对。
>
> `EasyTierConfig.cs` 保持 `no_tun = true`、`private_mode = true`，因此**不需要 root、TUN/TAP、驱动、防火墙规则**；Linux 分支 `IsWindows7 = false`，会启用 QUIC 分支。

## 本机验证结果（Windows 开发机，.NET 8.0.428 SDK 在 `C:\Users\yzhhh\.dotnet`）

阶段 1c（2026-11-14）：

```
dotnet build -c Release   →  0 警告 0 错误（含 LinkSession 本地副本与全部 shim）
campfire selftest         →  11 项全过，SELFTEST OK
campfire code / --check   →  生成 U/4KUY-3B4J-TUQ6-RD9Y，展开
                             network_name=scaffolding-mc-4KUY-3B4J / secret=TUQ6-RD9Y
campfire host / join      →  参数校验正确；在 Windows 上明确拒绝启动会话并退出 2
campfire status/peers/logs/stop/config/version/doctor → 全部通过
```

第 11 项自检会**实际 `new LinkSession(pump)`**（只构造、不联网），并验证替代 WPF `DispatcherSynchronizationContext` 的消息泵真的能 `Post` 入队、`PumpAll` 出队执行——防止「编译通过但编排层没被链进来」的假象。

阶段 1.5（2026-11-15，新增）：

```
两个工程 dotnet build -c Release  →  0 警告 0 错误
./verify-lobby.sh                 →  42 项全过，0 失败 0 跳过，LOBBY VERIFY OK
配置触发的 429 / 503              →  临时实例（5081 / 5082）验证通过
campfire lobby list               →  用官方 LobbyClient 解析器读到自建服务端房间
campfire lobby publish --once     →  POST + DELETE 全链路通过
campfire lobby join               →  密码校验与换码通过
```

> Windows 上只能编译、自检与跑 HTTP 服务端；`host` / `join`、EasyTier 二进制运行、与官方 Windows 客户端互通，必须在 Linux 上做。程序在非 Linux 平台会明确拒绝启动会话，而不是卡在下载组件上。

## 免剪贴板工作流（Bazzite / VMware 必看）

VMware 的拖放、共享文件夹、**剪贴板共享**都依赖客户机 `open-vm-tools-desktop` 的 `vmware-user`；Bazzite 是不可变系统 + Wayland，默认没有这套东西。主机复制的新命令根本同步不进虚拟机，粘贴只会得到缓存的旧命令（表现为「一粘贴就变成最早那条装 SDK 的命令」）。因此每一步都做成「HTTP 目录里的脚本 + 一行 curl」，只需手敲一行：

> ⚠️ **`<主机IP>` / `<IP>` 是占位符，敲命令时要连尖括号一起换成服务窗口打印的数字**（例如 `192.168.1.5`）。
> bash 里 `<` / `>` 是重定向符号，照抄尖括号会报 `bash: 192.168.1.5: 没有那个文件或目录`——
> 注意报错前缀是 **`bash:` 而不是 `curl:`**，说明命令在 shell 解析阶段就被拆了，curl 根本没启动，
> 跟网络、服务、脚本都无关。在 IP 中间加空格只会让报错变成 `bash: 192.: 没有那个文件或目录`。
> 另外命令里尽量别出现 `-` 开头的选项组合：手敲漏横杠会把 `fss` 当成主机名。

| 步骤 | 一行命令 | 脚本做了什么 |
|---|---|---|
| ② 装 EasyTier | `curl 192.168.1.5:8000/step2.sh \| bash` | 找主机服务、检查 SDK/编译产物、写 PATH 与 alias、下载并校验 2.6.4、安装、`check`、`doctor` |
| ③ 回环自检 | `curl 192.168.1.5:8000/step3.sh \| bash` | 自己起 host + join 两个会话，从 `session.json` 读邀请码写入 `~/cf-code.txt`，打印两端 `status`/`peers`/日志 + 明确 PASS/FAIL 结论后自动停止 |
| ③ 排障（只读） | `curl 192.168.1.5:8000/step3-diag.sh \| bash` | 检查 `easytier-core` 能否运行、公共节点 TCP、两端 `Link/link.log` 错误行与尾部、残留进程 |
| ④ 双向互通（v2） | `curl 192.168.1.5:8000/step4.sh > step4.sh` 然后 `bash step4.sh`（两条都要敲；**别用 `-fsS`，手敲漏横杠会报 `Could not resolve host: fss`**） | 后台跑 host/join + 日志落盘 `~/cf-host.log` / `~/cf-guest.log`，每 3 秒打印阶段与玩家数；会话意外退出时自动打印退出码、错误行和日志尾部 |
| ⑤ GUI 编译与启动 | `curl 192.168.1.5:8000/step5.sh \| bash` | 更新源码包、离线 NuGet（167 MB，已装好就跳过）、离线 restore、三工程编译 + 11 项自检、字体/DISPLAY 检查、启动 GUI |
| ⑥ 装完整 JDK | `curl 192.168.1.5:8000/step6.sh \| bash` | 下载并校验 Temurin 17（185 MB）、解压到 `~/.local/campfire-jdk`、验证 `jdk.attach@`、写 `campfire-jdk-env.sh` 并让自动开放入口加载它 |

> 表里 `192.168.1.5` 只是当前实测地址，**一律以服务窗口打印的「本机 IP」为准**（DHCP 会变）；照抄时不要带 `<` `>`。所有脚本内部也会自动探测网关与历史地址，可用 `CF_HOST_IP=实际IP bash stepN.sh` 显式指定。

两个脚本放在 `linux/`（也就是 `serve-for-vm.py` 的 HTTP 根目录），带 GPL 修改声明、LF 换行，`bash -n` 通过；只写 `$HOME`，不改系统层、不需要 sudo。③ 加了 `--skip-probe`、没有真实 MC 世界，只验证「邀请码 → EasyTier 组网 → 房间协议」这条链路；完整日志留在 `~/cf-a.log`、`~/cf-b.log`。

**③ 通过的唯一判据**（不要只看 `status` 的某一瞬间快照，它每 2 秒才写一次）：控制台出现 `========== 已加入房间 ==========` 横幅、`Found host scaffolding-mc-server-* (direct, tcp)`、`Host game port ... is reachable at local port ...`、`Joined room U/...`，以及 `[HH:MM:SS] 就绪 | ... 玩家 2`。`CliHost` 只在 `LinkStage.Ready` 时才打印该横幅，所以有横幅即通。

链路日志里几种**无害噪音**，别误判成失败：

| 日志 | 原因 |
|---|---|
| `WARN 官方 v2.6.4 Release 未提供 Linux 校验和` | 上游确实没给 checksum；主机侧已用 `sha256sum -c` 自行校验过 |
| `WARN The running game was not found; the in-game overlay agent can not be attached` | `--skip-probe` + 虚拟机没装 MC；游戏内覆盖层（Java Agent）本就在裁剪清单里 |
| `Connection refused (os error 111)` / `failed to get manage client` / `tcp://127.0.0.1:<随机端口>` | `easytier-core` 刚启动时 RPC 管理端口还没监听，轮询抢跑了几拍；随后连上才打印 `Found host`。统计列因此可能是 `节点 -1 / 直连 0`，但**实际网络是通的**（有 `direct, tcp` 与端口映射） |
| `Network instance stopped.` / `Room stopped by the user.` | `step3.sh` 结束时自动 `stop`，正常清理 |

> 正式与官方 Windows 客户端互通时**不要**加 `--skip-probe`，且要先在 Minecraft 里点「对局域网开放」，再把端口传给 `host --port`。只验证房间层邀请码互通时可以保留它。

### ④ 到底需不需要 Minecraft？分方向看（重要）

**方向 A（Linux 建房 → Windows 官方客户端加入）：不需要，零下载。**

读 `LinkSession.StartJoin`（原文件 459–541 行）可知，加入方完全不查本地世界：装组件 → 加入 EasyTier → `FindHostAsync` → 端口转发 → 连 `RoomServer` → 发心跳 → 拉玩家列表 → `Ready`。唯一的探测是 `MinecraftProbe.IsServerAsync(localPort)`，探的是**通过隧道转发到房主那边的游戏端口**，而且连不上也只是 `LinkLog.Warn(... continuing anyway)`（514–515 行），不阻断。诊断页那两个灰点文案写的是「**开房前**」「**房主**进入世界后」，都只约束建房，且是 `DiagState.Info` 而非 Fail（`Diagnostics.CheckGame` / `CheckWorld`）。

**方向 B（Windows 官方客户端建房）：需要本机有一个「开放局域网的世界」。**

`StartHost` 会 `await LocalWorldFinder.WaitForLocalWorldAsync(...)`，`MonitorHostAsync` 还会每 6 秒探一次世界端口，连续 3 次失败就抛 `Link.Error.WorldClosed` 关房。没有 MC 启动器/客户端时，用下面的验证桩代替，不必下载几百 MB。

### 零下载：`windows-test/` 假世界验证桩

`LocalWorldFinder` 的判定只有三条（已逐条实测）：

| 条件 | 源码 | 验证桩如何满足 |
|---|---|---|
| 端口由**进程名以 `java` 开头**的进程监听 | `IsJava(pid)` → `ProcessName.StartsWith("java")` | 用 `C:\jdk-17\bin\java.exe` 启动（本机已有 Temurin 17.0.20.1） |
| 发 `0xFE` 回 `0xFF` | `MinecraftProbe.IsServerAsync` | `FakeMcWorld.java` 回单字节 `0xFF` |
| 发 `0xFE 0x01` 回 1.4 版 legacy ping | `MinecraftProbe.GetStatusAsync` | 回 `0xFF + 2 字节 BE 长度 + UTF-16BE`，字段 `§1 / 47 / 1.20.1 / MOTD / 0 / 20` |

用法（Windows 本机，双击即可，**窗口要一直开着**）：

```
windows-test\start-fake-world.bat
```

它会自动编译并启动 `127.0.0.1:25565`。另开终端跑 `python windows-test\verify.py` 应看到两行 `OK` + `PASS`；再到 Campfire 的「联机诊断」点「重新检测」，「局域网世界」会变绿并显示端口 25565。「游戏」那项仍是灰点（`CheckGame` 只看账号，是 Info 级），不影响。

实测记录：`IsServerAsync → reply=ff`；`GetStatusAsync → parts=['§1','47','1.20.1','Campfire Linux Port Test','0','20']`；`netstat` 显示 `127.0.0.1:25565 LISTENING`，`tasklist` 显示进程名 `java.exe`。

> **局限，别误会**：这是协议桩，**不是能玩的 MC 服务器**，没人能真的进世界。阶段 1 只验「邀请码 → EasyTier 组网 → 房间协议 → 玩家列表」这条链路，桩够用；真要打游戏还是得装 MC，并且 Linux 端去掉 `--skip-probe`。

### 「等 10 秒就没了」不是 Linux 端口的 bug

源码里 `RoomServer.GuestTimeout = 10s`，`MonitorHostAsync` 每 2 秒调一次 `RemoveSilentGuests()`；加入方 `LinkSession` 注释写得很直白：`// Heartbeat every few seconds; the host drops players silent for 10 seconds.`，每 4 秒发一次 `PlayerPing` 心跳。也就是说：

- 加入方一旦停止心跳（Windows 客户端因为没进世界而退出房间、或网络断了），**10 秒后房主就把它从玩家列表里删掉**，看起来像「玩家没显示 / 房间停了」。
- 房主本身不会因此关房。`--skip-probe` 已经短接了 `MonitorHostAsync` 里的 `MinecraftProbe.IsServerAsync`，所以 Linux 房主不会触发 `Link.Error.WorldClosed`。
- **但 Windows 官方客户端作为房主时没有这个短接**：`MonitorHostAsync` 每 6 秒探一次世界端口，连续 3 次失败就 `Link.Error.WorldClosed` 关房。这才是「建房后没多久自己停了」的更常见原因，`windows-test/` 的假世界桩正好让它一直探得到。
- 若 Linux 端进程真的退出，那一定是 `LinkStage.Failed`（`CliHost.Drive` 会打印「联机失败：…」并退出）。v2 脚本会把这行、退出码和日志尾部一起打印出来，不用再靠截图猜。

## 后续阶段（按顺序）

1. ~~**阶段 1 验收（唯一硬标准）**~~ → **已于 2026-11-18 完成**，见开头「阶段 1 验收结果」。复现：
   ```bash
   curl 192.168.1.5:8000/step4.sh > step4.sh   # IP 换成服务窗口打印的
   bash step4.sh          # 选 1 = 方向 A；选 3 = 方向 B
   ```
   方向 B 之前先在 Windows 双击 `windows-test/start-fake-world.bat` 并**保持窗口开着**（关掉后房主会 `WorldClosed` 自动关房）。Windows 端问 `campfire:mods` / `voice` 收到「模组不可用」是**预期降级**，不算失败。「公开到联机大厅」按钮不要点（源码构建 `IsAvailable=false`，且按约定不连作者官方服务）。
2. **阶段 2（分两批）**：Avalonia 界面重建。原桌面 UI 是 WPF/XAML（28 个 XAML / 约 1.9 万行），Linux 无法编译；阶段 1 已把 `Link/` 协议层完整复用进 `net8.0` CLI。

   **第一批（已交付）**：`linux/Campfire.Avalonia/` —— 共享源码链接复用协议层（不引用 `campfire.dll`）、`SessionService` 长驻单例直连 `LinkSession.Changed`、静态篝火橙配色（照抄 `Colors.xaml`）、680×598 自绘标题栏 + 10 层阴影 + 4 个胶囊 Tab、设置页 132px 侧栏 6 分区、自建大厅 list + join。地图/工具 Tab 按约定保留结构 + 说明条，个性化分区先放说明条。

   **第二批（已交付，2026-11-19）**：完整动态主题（OKLCH 算法 + 9 种子色 / 6 风格 / 深浅两套 / 400ms 过渡、gsettings 深浅、GNOME 强调色、SkiaSharp 壁纸取色）、设置页个性化分区重建、地图 Tab 本机世界只读扫描、工具 Tab 组件自检 / 大厅启停 / 日志暂停。详见下面「动态主题」章节。

   ### 第一批怎么跑到虚拟机里（一行，免剪贴板）

   Windows 侧保持 `python linux/serve-for-vm.py` 窗口开着，Bazzite 终端敲：

   ```bash
   curl 192.168.1.5:8000/step5.sh > step5.sh   # IP 换成服务窗口打印的
   bash step5.sh
   ```

   **两条都不要带 `<` `>` 尖括号，也不要带 `-fsS`**（见本节开头的警告）。一行版也可以：`curl 192.168.1.5:8000/step5.sh | bash`。

   `step5.sh` 会更新源码包、下载并校验离线 NuGet 包（约 167 MB）、生成运行时 `NuGet.Config`、离线 restore、全量编译、`fc-list` 检查中文字体，最后前台启动 GUI（Ctrl+C 结束）。脚本内部会自动探测主机服务地址，候选含默认网关与 `192.168.1.5` / `172.16.202.127`；若主机 IP 都不在列表里，用 `CF_HOST_IP=实际IP bash step5.sh` 显式指定。以后启动 GUI 用短命令 `bash ~/campfire-gui.sh`。

   > 为什么需要离线包：Avalonia 11 是本分支**首次引入 NuGet 依赖**，虚拟机直连公网慢/断。25 个 `.nupkg` 全闭包放在 `linux/offline-nuget/`，压成 `offline-nuget.tar.gz` 单独走 HTTP，不进源码 tarball（`pack-for-linux.sh` 已排除）。`NuGet.Config` 带 `<clear/>`，只在 VM 运行时生成、不入库，否则会挡住 Windows 侧联网还原。
   > GUI 走 XWayland，需要有效 `DISPLAY` 与合成器；透明窗口异常时加 `XLIB_SKIP_ARGB_VISUALS=1`。Linux 没有微软雅黑，字体按 Noto CJK → 思源黑体 → 文泉驿 → Inter 回退。

   ### 界面还原补充：全屏适配 + 动态营火图标（2026-11-19）

   用户反馈「全屏会畸形」，并要求把标题栏静态火焰换成动态图标（用户提供 `Campfire_JE2_BE2.gif`）。两处都已完成：

   - **动态营火**：该 GIF 是 300×300 / **40 帧 / 每帧 100 ms / 带 NETSCAPE 循环标记**。用 Python 标准库（不引入任何第三方依赖）解码，逐帧缩放到 96×96 后横向拼成 `Assets/CampfireFrames.png`（3840×96，13 KB，四角全透明），以 `EmbeddedResource` 嵌进 dll。`Controls/CampfireLogo.cs` 用 `DispatcherTimer` 按 100 ms（10 fps，与原版 `Controls/CampfireLogo.cs` 一致）裁剪帧条绘制，窗口不可见时自动停表。原版那两层 Path 火焰已删除。素材版权见 `THIRD-PARTY-NOTICES.md`。
   - **全屏布局**：主页 3 张大磁贴改成 `Grid ColumnDefinitions="*,10,*,10,*"` 等宽铺满 + `MinHeight="124"`（窗口拉高时不变扁），下排 2 张各占一半；限宽居中从 `HomeView` 上提到 `MainWindow` 的 `ContentControl`（`MaxWidth="1400"`），**所有 Tab 统一生效**，避免地图/工具/设置页在超宽屏上文字挤在左边。另去掉了未生效的垂直居中代码。
   - **窗口按钮对齐**：原版 `MainWindow.xaml:151/166` 的减号/叉号 Path **不设 Width/Height**，WPF 按几何边界自动居中；初版移植给每个 Path 都套了 `Width="10" Height="10" Stretch="None"`，导致 `M0,0 L10,0` 那条减号被固定在 10×10 框**顶部 y=0**，比居中的方框 `Rectangle`、叉号高了约 5px（用户反馈「不齐」）。现改为统一 12×12 框、几何中心都在 (6,6)：减号 `M1,6 L11,6`、叉号 `M1,1 L11,11 M11,1 L1,11`，方框保留原版 10×10 微圆角 `Rectangle`；并还原原版的 `StrokeLineCap="Round"` 与第 2/3 个按钮 `Margin="4,0,0,0"`。2880×1704 截图裁切放大实测三者已对齐。
   - Windows 开发机以 **2880×1704 最大化实测**：主页选中、磁贴等宽无错位、营火像素动画显示正常、中文无方块。内容下方留白是全屏下内容不足的正常现象，不是畸形（原版窗口固定 680×598 且不可缩放）。

   **有意不还原**：下排首卡是「联机诊断」而非「最近房间」（没有房间历史）。右上「繁」圆按钮已于 2026-11-26 还原并升级为 **简 / 繁 / EN 三语切换**，见「界面本地化」一节。

   ### 第一批验收：GUI 双向互通回归（2026-11-19 已通过 ✅）

   阶段 1 的双向互通原本用 CLI（`step4.sh`）验；GUI 的 `SessionService` 于 2026-11-19 按下面清单在 VM 里完成方向 A / B 双向回归，两端均为「玩家 2 / 直连」。以下保留为可复现的验收清单。

   先确认没有残留会话（两个 EasyTier 实例会抢同一个虚拟网络名）：

   ```bash
   pgrep -a easytier-core
   ```

   有输出就先停掉（`bash step4.sh` 的窗口按 Ctrl+C）。然后启动（自检模式，默认不勾「自动开放局域网世界」、端口 43210）：

   ```bash
   bash ~/campfire-gui.sh
   ```

   **方向 A：Linux GUI 建房 → 官方 Windows 客户端加入**

   1. GUI 主页点「创建房间」磁贴 → 建房页，端口保持 `43210`，**不要勾**自动开放 → 点「开始」。
   2. 「邀请码」卡片出现大字 `U/XXXX-XXXX-XXXX-XXXX`。
   3. Windows 双击 `C:\Users\yzhhh\Downloads\Campfire.exe` →「加入房间」→ **手敲**那个码（剪贴板不可用）。
   4. 判据：GUI「房间状态」显示 `玩家：2`，Windows 端玩家列表 2 人 / 直连。

   **方向 B：官方 Windows 客户端建房 → Linux GUI 加入**

   1. GUI 点「停止」。
   2. Windows 双击 `windows-test\start-fake-world.bat` 并**保持窗口开着**，官方客户端诊断页「重新检测」→「创建房间」，记下邀请码。
   3. GUI 主页点「加入房间」→ 输入邀请码 → 点「开始」。
   4. 判据：GUI「房间状态」显示 `玩家：2` / 阶段 `Ready`，Windows 端玩家 2。

   **两个方向都通 = 第一批完成**（2026-11-19 用户实测：方向 A / B 均 `玩家 2 / 直连`，已闭环）。自检模式只验房间层（邀请码 → EasyTier 组网 → 房间协议 → 玩家列表），进 MC 世界仍不行（VM 没装 MC），属预期降级。

   **回归时发现并修复的 3 个 UI bug**：

   | 现象 | 根因 | 修复 |
   |---|---|---|
   | Windows 端退出 / 点「停止」后仍显示「玩家 2」和上一次的邀请码 | `LinkSession.Stop()`（:1332-1342）只重置 `Role/Stage/Error`，**不清** `Players/Code/DirectPeers/ConnectedNodes/LocalPort`；`SessionService.Refresh()` 又无条件照抄 | `Refresh()` 在 `!IsActive` 时调 `ClearVisualState()` 归零；`Host/Join/Stop` 开头也先清；**协议层副本一行未改** |
   | 「加入房间」页冒出一张「邀请码」卡片，还写着「把这个码手敲进 Windows 官方客户端」 | `StartJoin`（:475）`Code = code` 把**输入的邀请码**存进 `Code`，而该卡片没有 `IsVisible` 条件 | 卡片加 `IsVisible="{Binding Hosting}"`，文案改「发给朋友」 |
   | 两页除了标题汉字几乎没区别 | 两页共用 `LinkView`，下面三张卡片完全相同 | 加入页新增「连接信息」卡片（`127.0.0.1:<LocalPort>`，未就绪显示「等待房间就绪…」）、按钮改「加入房间 / 退出房间」；建房页保留邀请码卡片；「本机连接端口」行只在 `HasConnection` 时显示 |

   验证：反射探针模拟残留字段后 `Refresh()` 全归零（PASS）；实际 GUI 截图确认加入页无邀请码卡片、有连接信息卡片、状态「未开始 / 已停止」、玩家 0。

   > 并行线（不阻塞第一批）：VM 里装真实 MC + 完整 JDK 才能实测「自动开放局域网世界」端到端。`step6.sh` 已装好 JDK、`GameMappings` 已移植，代码侧三块前置齐备，只差游戏本体。

## 动态主题（阶段 2 第二批，2026-11-19 已交付）

### 文件与逐字移植范围

| 文件 | 来源 | 改动 |
|---|---|---|
| `Campfire.Avalonia/Themes/ColorMath.cs` | `src/Campfire/Themes/ColorMath.cs` | **逐字移植**：Oklch / sRGB↔Oklab 矩阵 / gamma / 色域二分 18 次 / Parse / Contrast。仅把 `System.Windows.Media.Color` 换成 `Avalonia.Media.Color`（`FromRgb` / `FromArgb` / `R,G,B` 签名相同） |
| `Campfire.Avalonia/Themes/ThemeManager.cs` | `src/Campfire/Themes/ThemeManager.cs` | `ThemeMode` / `PaletteStyle` / 9 个预设 / `Palette.Create` / `FillLight` / `FillDark` / `SetRamp` / `SetGrays` **逐字移植**；设置键名 `theme.mode/monet/monet.index/color/style` 不变，直接复用 Linux 版 `AppSettings.Get/Set` |
| `Campfire.Avalonia/Themes/LinuxThemeSources.cs` | `src/Campfire/Themes/WallpaperColors.cs` | `Score()`（360 色相分箱、覆盖率/彩度评分、90→15 度间距挑 4 个候选）与 `SampleSize=128` / `MaxColors=4` **逐字移植**；Win32 / 注册表调用全部替换 |
| `App.axaml.cs` | — | 启动时 `ThemeManager.Initialize()`，退出时 `Shutdown()` + `Session.Shutdown()` |
| `Views/PreferencesView.axaml` + `ViewModels/Pages.cs` | `Pages/PreferencesPage.xaml(.cs)` | 132px 侧栏 6 分区壳不动；个性化分区重建为 5 张卡片（模式 / 风格 / 9 色板 + 自定义 hex / 壁纸取色 / 当前状态） |

### Linux 替代实现（与 Windows 原版的差异，如实清单）

| 原版（WPF） | Linux 版 | 差异说明 |
|---|---|---|
| `Registry ... AppsUseLightTheme` | `gsettings get org.gnome.desktop.interface color-scheme` → `gtk-theme` → `GTK_THEME` → 浅色 | 逐级降级；没有 GNOME 时一律按浅色 |
| `SystemEvents.UserPreferenceChanged` + 600ms 去抖 | `System.Threading.Timer` **3 秒轮询**，值变化才 `Apply(true)` | 系统深浅切换最多延迟 3 秒，不是即时事件；轮询在线程池跑，不卡 UI |
| `DwmGetColorizationColor`（任意强调色） | `gsettings ... accent-color` 映射到 **GNOME 9 色枚举**（blue/teal/green/yellow/orange/red/pink/purple/slate） | 只能取 9 个预设色，取不到回退篝火橙 `#E2680C` |
| `SystemParametersInfo(GetDeskWallpaper)` + GDI+ 缩放 | `gsettings ... picture-uri(-dark)` → `file://` 转路径 → **SkiaSharp 2.88.9** 解码缩放 128×128 | 只覆盖 GNOME 静态壁纸；幻灯片 / KDE / Wayland 壁纸协议未覆盖，另有 `~/Pictures/wallpaper` 等兜底路径 |
| `Registry Control Panel\Colors\Background`（纯色桌面） | `gsettings ... primary-color` | 格式不同，语义等价 |
| `SolidColorBrush.BeginAnimation(ColorAnimation, 400ms CubicEase)` | `DispatcherTimer` **20ms 逐帧插值**，每帧替换 `Application.Resources` 顶层条目 | 视觉效果等价，但不是 WPF 的渲染层动画；切换时会有约 40 个键 × 20 帧的字典写入 |
| `Window.Resources` 覆盖 | `Application.Current.Resources` 顶层覆盖（先于 `MergedDictionaries` 命中） | — |

**未 100% 还原的部分**：DWM 任意色、注册表、系统主题即时事件、GDI+ 缩放、`Color1..8` 作为 `StaticResource` 被引用时不会随动态值变化（UI 实际用的是 `Brush1..8`，已覆盖）。`BrushWhite` 保持白色（标题栏文字在两套主题下都可读），与原版一致。

### 地图 / 工具 Tab 真功能

- **地图**：`WorldScanner` 只读枚举 `~/.minecraft/saves`、PrismLauncher / MultiMC / Flatpak 实例的 `saves`，列出世界名与 `level.dat` 修改时间；「导入地图」页列出全部检测路径。**刻意不写存档**（不做解压导入），避免误改用户的世界。
- **工具**：`EasyTierPackage.CanRunAsync()` 组件自检按钮、`LobbyService` 的 HTTP 状态探测 + 启动/停止（**只停止本 GUI 自己启动的进程**，绝不 kill 别处起的服务）、日志「自动刷新」复选框真正暂停 `SessionService` 的 1 秒轮询。

### 验证

`bash build.sh` 三工程 0 警告 0 错误 + selftest 11 项全过；Windows 开发机实启动 GUI 截图核对：默认浅色橙、`theme.mode=dark` + 蓝色种子 + `Vibrant` 深色界面、设置页点第 5 个色板实时变蓝（`Apply(true)` + 400ms 过渡）、地图页 / 工具页新控件全部渲染正常。

## 自动开放局域网世界（Java Agent，2026-11-19 恢复）

**为什么阶段 1 要手动开**：当时把 `JavaAgent` / `LanOpener` / `Worlds.RunningGame` 当成「Windows 专属」一起裁掉，并把 `LinkSettings.AutoOpenLan` 强制设成 `false`。复查源码后确认这是**过度裁剪**：

- 原版 `AutoOpenLan` **默认 `true`**（`LinkSettings.cs:81-85`）。
- Java 侧 `src/java-agent/` 的 3 个 jar 与 5 个 `.java` 是**纯 JVM**：`com.sun.tools.attach.VirtualMachine.attach(pid)` 注入后，反射调用游戏自己的 `publishServer(...)` —— 也就是「对局域网开放」按钮背后的那个方法。整套在 Linux 上同样成立。
- `LanOpener.cs` / `AgentState` 只是往数据目录写 `lan-<pid>.txt` / `overlay-<pid>.txt` 并每 3 秒刷新，**零平台依赖**。
- `LinkSession.cs` 的 Linux 副本里 `OpenWorldToLanAsync` **本来就完整保留**（阶段 1 只加了 `SkipProbe`），所以协议层一行都没改。

### 本分支补的 5 处 Linux 适配

| 原 Windows 实现 | Linux 版 |
|---|---|
| `JavaAgent.FindAttachJava` 找 `java.exe` / `Program Files` / 注册表 | `Linux/GameAgent.cs`：找 `CAMPFIRE_ATTACH_JAVA`、`JAVA_HOME/bin/java`、`/usr/lib/jvm/*/bin/java`、启动器 runtime、PATH，逐个 `--list-modules` 检查 `jdk.attach@` |
| `Worlds.RunningGame.Find()` 用 WMI 枚举进程 | `Shims/Worlds.cs`：读 `/proc/<pid>/exe` + `/proc/<pid>/cmdline`，认 MC 主类或 `--gameDir` |
| `LocalWorldFinder` 用 `iphlpapi.dll` 找端口 | `Linux/LocalWorldFinder.cs`：`/proc/net/tcp(6)` 的 LISTEN inode → `/proc/*/fd` → 只认 MC 进程持有的 socket |
| 3 个 jar 是 `EmbeddedResource` | 两个 csproj 各自嵌入；`pack-for-linux.sh` 把 `src/java-agent/` 源码与 jar 一起收进部署包（GPL-3.0 完整对应源码） |
| `GameMappings.FindVersionFile` 用 `;` 与 `\libraries\` | `Linux/GameMappings.cs`：classpath 按 `:` 拆、认 `/libraries/`，沿版本 json 的 `inheritsFrom` 找 `downloads.client_mappings` 的 URL/SHA-1；BMCLAPI 镜像优先、单源超时 30s、按内容哈希缓存。`GameAgent.LoadAsync` 把结果拼成 `state=...\|mappings=...` 传给 Agent |

### 怎么用

**① 装完整 JDK（一次即可，约 185 MB）**

Windows 侧先跑 `bash linux/fetch-jdk.sh`，把 Temurin 17.0.21+9 的 Linux x64 tarball 下到 HTTP 目录（`linux/`），并保持 `serve-for-vm.py` 窗口开着。虚拟机里手敲一行：

```bash
curl 192.168.1.5:8000/step6.sh | bash
```

IP 以服务窗口打印的为准，别套 `<` `>`。`step6.sh` 会下载 + `sha256sum -c` 校验 + 解压到 `~/.local/campfire-jdk`（`--strip-components=1`），验证 `bin/java` 能跑且 `--list-modules` 里有 `jdk.attach@`，然后写 `~/campfire-jdk-env.sh` 并重写 `~/campfire-gui-autolan.sh` 让它自动加载 `CAMPFIRE_ATTACH_JAVA` / `JAVA_HOME`。已装好并校验通过就跳过下载。`step5.sh` 生成的入口也会 source 这个片段，所以两个脚本谁先跑都行。

> **版本提醒**：这个 JDK 17 只适用于 1.18–1.20.4（以及作为旧版 MC 的 attach 端）。**Minecraft 26.1 起要求 Java 25**，装 26.3 请直接走下下节的 `step7.sh`，它会装 Temurin 25 并生成 `campfire-gui-autolan-26.sh`，不覆盖这里的旧入口。

**② 启动正式联机入口**

```bash
bash ~/campfire-gui-autolan.sh
```

这个入口不跳过世界探测，并默认勾上「自动开放局域网世界」。建房页勾上后进入一个单人世界，Campfire 会自动开放并发现端口，**不用手点、不用填端口**。老的 `bash ~/campfire-gui.sh` 是房间层自检模式，勾选会被明确拦下，不会卡死。

也可手动设 `CAMPFIRE_ATTACH_JAVA=/path/to/jdk/bin/java`，日志会打印选中的 java。工具 Tab 有「自动开放局域网（Java Agent）」卡片，显示开关、jar 与环境变量状态。

### 硬前提与已知边界（诚实清单）

- **必须有真实 Minecraft**：`windows-test/` 的假世界桩只能骗过「已开放局域网」的探测，没有游戏内 `publishServer` 可调。2026-11-21 已按用户指定的 **26.3** 备好离线包（`mc-26.3-offline.tar.gz` + JDK 25 + `step7.sh`），装完即可在 VM 里跑真游戏。
- **必须有带 `jdk.attach` 模块的完整 JDK**：MC 启动器自带的通常是 JRE，没有这个模块；Bazzite 不可变系统也不能 `dnf install`。已备好 `fetch-jdk.sh` + `step6.sh`：Windows 侧从清华 Adoptium 镜像下 Temurin 17.0.21+9（与本机 Windows JDK 同大版本），走已验证的 HTTP 通道送进 VM，解压到 `~/.local/campfire-jdk` 并自动设好 `CAMPFIRE_ATTACH_JAVA`。不需要 sudo、不改系统层。
- **同一用户、目标 JVM 未禁 attach**：Linux attach 走 `/tmp/.java_pid<pid>`，沙箱或 `-XX:+DisableAttachMechanism` 会挡住。
- **MC 版本与混淆**：Hook 支持 1.14–26.x，按官方名反射查找方法。按 `CampfireNames.java:22-24` / `GameMappings.cs:15`，**1.14.4–1.21.11 的官方启动器正式版都是混淆版**（Mojang 另发 `client_mappings` 供还原），**26.1 起才不混淆**；Fabric/Forge 等以 Mojmap 官方名运行的加载器也不需要映射。本分支已移植 `Linux/GameMappings.cs`：从 `/proc/<pid>/cmdline` 拿 classpath、沿版本 json 的 `inheritsFrom` 找 `downloads.client_mappings` 的 URL/SHA-1，优先走 BMCLAPI 国内镜像（单源 30s），按 SHA-1 缓存到 `DataDirectory/mappings/`；缓存里已有同名文件就直接复用，**可在 Windows 侧预下好后放进 VM 的数据目录，完全不用联网**。1.7.2–1.14.3 的老版本名字表随程序内嵌（`Mappings/*.txt`）。若所有下载源都失败，日志会警告并退回「只按官方名查找」，功能降级但不卡死。
- 失败不会卡死：`OpenWorldToLanAsync` 会打日志并把 `AutoLan` 标成 `Unsupported`；取消勾选、手动填端口仍可正常联机。
- **Linux 端到端已真机确认**（2026-11-25）：26.3 的元数据、Hook 反射目标、ASM 对 Java 25 字节码的支持先由 Windows 侧静态验证，随后在 Bazzite 用 `step7.sh` + 单人世界 + `campfire-gui-autolan-26.sh` 实测通过，Windows 官方客户端 `0.8 ms` 直连、两端 2 人。详见「Minecraft 26.3 端到端实测」一节。

## Minecraft 26.3 端到端实测（2026-11-25 已通过 ✅）

> **2026-11-25 16:58 真机结果**：MC 26.3 窗口标题 `Minecraft 26.3 - Multiplayer (LAN)`、聊天栏「Campfire 已自动对局域网开放，端口 …」；GUI 建房页 `阶段 Ready · 玩家 2 · 直连 1 · 中转 0 · 节点 4`；Windows 官方客户端 `127.0.0.1:42485`、`0.8 ms`、`直连 · TCP`、玩家 2。**自动开放局域网端到端通过，项目全部闭环。** 离线账号下 26.3 的 `prepareKeyPair()` 也正常，此前标为最大未知的风险已排除。

用户指定 **Java 版 26.3**（正式版，2026-09-15 发布，Wilderness Bound / 奔赴荒野）。2026 年起 Mojang 按年份编号，26.1 / 26.2 / 26.3 是当年三个 game drop；**1.21.11 是最后一个旧编号、也是最后一个混淆版**。

### 静态预检结论（Windows 侧，未启动游戏）

| 检查项 | 结果 |
|---|---|
| 版本元数据 | `26.3.json` → `javaVersion.majorVersion=25`；`downloads` 只有 client / server，**没有 `client_mappings`** |
| 混淆 | 26.1 起 Mojang 取消混淆 → **26.3 不需要任何映射表**，40 个 `Mappings/*.txt` 与本版本无关 |
| Hook 目标 | `javap` 打开 26.3 client.jar：`IntegratedServer.publishServer(MultiplayerScope,int)` 仍在（字节码调 `startTcpServerListener` 并写 `publishedPort`）；`tickServer(BooleanSupplier)`、`storageSource`、`HttpUtil.getAvailablePort()`、`ClientboundTabListPacket(Component,Component)` 都在 |
| ASM | 9.11.1 的 `ClassReader` 实测能读 **class file major 69**（Java 25），Transformer 不会因新字节码崩溃 |
| 结论 | **Java Agent 与 C# 侧一行都不用改**，缺的只是真实游戏 + Java 25 |

> 注：`publishServer` 在基类 `MinecraftServer` 里是三参 `(MultiplayerScope,boolean,int)`，但 Hook 在运行时类 `IntegratedServer` 上查找，那里保留了两参重载，所以「26.2+ 分支」能命中。26.3 的该方法在开放前会调用 `ClientPacketListener.prepareKeyPair()`；**2026-11-25 真机实测离线账号下正常通过**。

### 交付物

| 文件 | 大小 | 说明 |
|---|---|---|
| `linux/fetch-mc263.py` | — | Windows 侧预下器，可复现、断点续传、逐文件 SHA-1 校验 |
| `linux/mc-26.3-offline.tar.gz` | 497 MB | 版本 json + client.jar 40 MB + 66 个 Linux 库 80 MB + 12 个 natives `.so` + 资产索引 34 + 5149 个资源对象 465 MB |
| `OpenJDK25U-jdk_x64_linux_hotspot_25.0.5_8.tar.gz` | 135 MB | Temurin JDK 25（清华 Adoptium 镜像），游戏与 attach 共用 |
| `linux/step7.sh` | — | VM 侧一键安装 + 生成离线启动与联机入口 |

`fetch-mc263.py` 按版本 json 的 `rules` 只挑 Linux 库（114 → 66），把 `natives-linux` jar 里的 `.so` 解到 `natives/java`，并生成 `classpath.txt`。26.3 用 LWJGL 3.4.3 + **SDL3**（不再是 GLFW），natives 是独立 artifact，必须一并带上。

### 为什么必须换 JDK 25

`step6.sh` 的 Temurin 17 只覆盖 1.18–1.20.4；26.1 起游戏 class file 是 69，Java 17/21 都拒绝加载。`step7.sh` 把**游戏 JVM 与 attach JVM 统一到同一个 JDK 25**，既满足游戏要求，也避开「JDK 17 attach 到 Java 25 进程」的跨大版本风险。旧的 `campfire-gui-autolan.sh`（JDK 17）保留给旧版 MC，互不影响。

### VM 里怎么跑

```bash
curl 192.168.1.5:8000/step7.sh | bash
```

IP 以服务窗口打印的为准。之后开两个终端：

- **终端 A**：`bash ~/minecraft-26.3.sh` — 不开启动器、不要正版账号，直接按版本 json 拼 classpath 离线启动（`step7.sh` 默认已生成 X11 + llvmpipe 低配版）；`--gameDir ~/.minecraft`，所以地图 Tab 能扫到新建的世界。进主菜单 → 单人游戏 → 新建世界 → **保持世界开着**。
- **终端 B**：`bash ~/campfire-gui-autolan-26.sh` — `CAMPFIRE_AUTO_LAN=1` + 不跳过世界探测 + `CAMPFIRE_ATTACH_JAVA` 指向 JDK 25。
- GUI 主页「创建房间」→「自动开放」已默认勾上 →「开始」。

**判据**：GUI 工具页日志出现 `Java agent loaded into game process` 与 `Opened <世界名> to LAN on port N`，房间状态不再停在「等待世界端口」，Windows 官方客户端能加入并看到 2 人。

排错：认证报错试 `MC_USER_TYPE=legacy bash ~/minecraft-26.3.sh`；SDL3 起不来试 `MC_SDL_DRIVER=x11`（或加 `LIBGL_ALWAYS_SOFTWARE=1`）；GUI 找不到游戏先 `pgrep -a java`。

### 已知边界

- **端到端已于 2026-11-25 在 Bazzite 真机跑通**；上面的静态预检、离线包与低配参数都已由实测确认。
- 26.3 在 LAN 开放前调 `prepareKeyPair()`，**实测离线账号也能通过**；无需正版登录，也无需退回 1.21.11。
- VMware SVGA 软件渲染下 26.3 只能低配跑（X11 + llvmpipe + 800×600 + 低画质），只验功能、不评性能；`step7.sh` 默认入口已按此固化。
- Minecraft 客户端与资源版权归 Mojang / Microsoft；离线包由使用者本机下载、只在自己的 VM 内使用，**不随 `campfire-linux.tar.gz` 分发**。

### 真机踩坑：classpath（2026-11-21 已修）

第一次在 Bazzite 上跑终端 A 报：

```
错误: 找不到或无法加载主类 net.minecraft.client.main.Main
原因: java.lang.ClassNotFoundException: net.minecraft.client.main.Main
```

这是 `ClassNotFoundException` 而**不是** `UnsupportedClassVersionError`，所以与 Java 25 无关。根因是离线包的 `classpath.txt` 在 Windows 侧生成时踩了两个坑：

1. 用 `pathlib.Path` 拼路径 → 写出 352 个反斜杠 `\`，Linux 的 `-cp` 不认，66 个库和 client.jar 全都没进有效 classpath；
2. 用 `write_text` 写行尾 → CRLF，最后一个 jar 路径尾巴带 `\r`。

`fetch-mc263.py` 已改成**正斜杠字符串拼接 + 二进制写 LF**（`classpath.txt` 与说明文本都改）。重打的包 sha256 `18ffd826…0c0`，实测 67 个条目全部与包内路径精确匹配、0 反斜杠、0 CR。`step7.sh` 解压后也会自动自检并重建。

**已经装好的 VM 不用重下 497 MB 包**，跑一个约 4 KB 的修复脚本就地重建 `classpath.txt` 即可：

```bash
curl 192.168.1.5:8000/step7-fix.sh | bash
```

然后重新跑终端 A。这和之前 `.sha256` 的 CRLF 是同一类 Windows 文本/路径坑，当时只修了校验和文件，漏了 classpath。

### 真机踩坑：26.3 主菜单 UI 不渲染（2026-11-25 已解决）

VM 里游戏窗口能弹出来，但只剩一张模糊的 **Dappled Forest 主菜单全景**（26.3 换的新背景），LOGO、按钮、文字、HUD 一个都没有，画面一直不动 —— 这是**渲染层卡住**，不是"还在加载"。

查证到的 26.3 背景：

- 26.3 把窗口/输入库从 GLFW 换成 **SDL3**；官方快照就列过 Wayland 独占全屏必崩，Linux/Wayland 是高风险组合。
- 26.2 起支持 `--graphicsBackend`（`default` / `opengl` / `vulkan`）；**26.3 正式版默认仍是 OpenGL**，26.4 快照起才把默认改成 Vulkan。软件渲染设备可显式切到 Vulkan 试。
- 26.3 有已知 hang（Mojira MC-311807）：SDL3 的同步调用阻塞渲染线程，`latest.log` 会**突然停止、无异常、无崩溃报告**，与"卡住"表现吻合。
- VMware SVGA + llvmpipe 下另有一类慢/卡：官方建议 `LIBGL_ALWAYS_SOFTWARE=1`、渲染距离 2–4、窗口化、关 shader；也可在 `.vmx` 里开 3D 加速（`accel3d.enable` / `mip.avail`）。

`linux/step7-gfx.sh` 做的三件事：

1. **诊断**（把结果截图回传即可定位）：`pgrep -a java`、`latest.log` 尾部 25 行、`saves/` 世界列表、`options.txt`、`glxinfo`、Vulkan ICD。
2. **降负载**：备份并重写低画质 `options.txt`（渲染/模拟距离 2、30 FPS、关云/粒子/实体阴影/VSync、窗口化）。
3. **两个低配启动脚本**：`minecraft-26.3-low.sh`（XWayland + 强制 llvmpipe + 800×600 + `-Xmx1G`，沿用默认 OpenGL）与 `minecraft-26.3-vk.sh`（同配置但 `--graphicsBackend vulkan`）。两者都支持 `MC_QUICK_WORLD=<saves 里的文件夹名>` → `--quickPlaySingleplayer`，**主菜单点不动也能直接进世界**继续测自动开放局域网。

```bash
curl 192.168.1.5:8000/step7-gfx.sh | bash
```

先 Ctrl+C 结束卡住的游戏，再跑 `bash $HOME/minecraft-26.3-low.sh`；不行换 `-vk`。**2026-11-25 真机已确认：`-low` 能让主菜单完整渲染**（LOGO、Singleplayer 等按钮全可见），**不必切 Vulkan**；根因是 Wayland + SDL3 默认路径叠加 VMware 软件渲染负载。若仍救不了，务实退路是开 VMware 3D 加速，或退回 **1.21.11**（GLFW + JDK 21 路线仍在）先验证 Agent 逻辑，26.3 保留静态兼容结论。

主菜单出来后：点 **Singleplayer → Create New World**（世界名建议用简单英文）→ 进入世界并保持窗口 → 另一个终端跑 `bash $HOME/campfire-gui-autolan-26.sh` → GUI 创建房间（「自动开放」默认勾上）→ 开始。判据是工具页日志出现 `Java agent loaded into game process` 与 `Opened ... to LAN on port N`。若离线账号卡在 26.3 的 `prepareKeyPair()` 导致 `Open to LAN failed`，降级路径是手动点「对局域网开放」拿端口、取消勾选后手填端口联机。

## 界面动画（2026-11-25，完整版）

最初只有「营火 10fps 帧动画」与「主题 400ms 颜色插值」，页面/卡片/按钮都是瞬间出现。第一版补了三类轻量动效，本版把它扩成**统一的多通道时间轴**，在不增加重绘负担的前提下把该有的反馈都补齐。

### 动效清单

| # | 动效 | 触发 | 参数 |
|---|---|---|---|
| 1 | 窗口开场 | 启动 | PanRoot 淡入 + 0.972→1 缩放 + 8px 上浮，420ms QuintOut（阴影层一起动） |
| 2 | 页面入场 | 切 Tab / 进子页 / 返回 | 卡片错峰 46ms、单张 300ms；淡入（前 62% 完成）+ 18px 位移带 6% 回弹 + 0.965→1 缩放，QuintOut / BackOut |
| 3 | 入场方向感 | 同上 | Tab 右移 +16px / 左移 −16px、进子页 +30px、返回 −30px；由 VM 换页前告知 `NavDirection` |
| 4 | 卡片内容跟进 | 同 #2 | 卡片内前 5 个子元素再错峰 26ms、延迟 96ms、220ms 淡入 + 6px 上浮 |
| 5 | 状态点呼吸 | 常驻 | 空闲 2.8s 慢呼吸 / 连接中 0.95s 快脉冲 / `Ready` 常亮 |
| 6 | Ready 光环 | 会话进入 `Ready` | 标题栏圆环 0.6→2.8 倍扩散 + 淡出，620ms |
| 7 | 按钮三态 | 悬停 / 按下 / 松开 | 悬停 1.035（磁贴 1.018、色板 1.15、标题栏 1.10）+ 磁贴上浮 2.5px，190ms；按下 0.945，90ms；松开 BackOut 300ms 弹性回弹 |
| 8 | 点击涟漪 | 任意按钮按下 | 顶层 `FxLayer` Canvas 里按点击点生成圆形，0.08→1.26 扩散 + 淡出，480ms；取当前 `Brush3` 作色，不参与布局、不拦截命中 |
| 9 | 卡片轻抬升 | 指针进入 / 离开 | 1.004 缩放 + 上浮 1.5px，200ms（与入场通道共存） |
| 10 | 数值变化脉冲 | `pcl-live` 文本更新 | 1→1.10 钟形缩放 + 2.5px 浮起，260ms；用于房间状态、玩家数、连接地址 |
| 11 | 主题色板弹出 | 点击色板 | 1→1.20 脉冲 340ms，与 400ms 主题插值同步 |
| 12 | 邀请码打字机 | `Session.Code` 变化 | 逐字写入（26ms/字，约 0.5s），末尾轻微脉冲；关闭动画时仍显示全文 |
| 13 | 营火微摇曳 | 常驻 | 帧动画之上叠 ±0.7px、3.1s 正弦，只动 RenderTransform |

### 引擎结构

- **通道合成**：每个控件一组 `Channels`（`Opacity` × `ScaleTransform` × `TranslateTransform`），入场 / 悬停 / 脉冲 / 摇曳各写各的字段，`Apply()` 统一相乘。多条动画叠在同一控件上不会互相覆盖——这是能同时开 13 种效果而不打架的前提。
- **通用时间轴**：`Track` 带 delay / duration / easing（Linear、CubicOut、QuintOut、SineInOut、BackOut）/ complete 回调；`RunExclusive(owner, channel)` 保证同一控件同一通道只留最新一条（hover 与 press 不会互相打断）。
- **共享 30fps `DispatcherTimer`**：所有瞬时动画 + 持续项共用一个表；无 Track 且无持续项时自动停表。`CAMPFIRE_ANIM_FPS` 可设 15–60。
- **防重播**：`AttachedToVisualTree` 在换 Content 时可能触发两回，`Entered` 集合保证同一 View 只入场一次，否则动画时长翻倍、看起来像卡片迟迟不出现。
- **`FinishAll()` 只收尾 `enter` 通道**：换页时把上一页卡片推到终态，但不会掐断正在跑的窗口开场 / 光环 / 涟漪 / 打字机。
- **`Cleanup()`**：View 卸载时清理通道状态、事件挂载与残留 Track，避免持有已销毁控件。

### 硬约束与性能护栏

- **只改 `Opacity` 与 `RenderTransform`，不碰任何随主题变化的画刷**。`ThemeManager` 每 20ms 替换 `Application.Resources` 顶层条目做 400ms 插值，给 `Background` 挂过渡会动画套动画、颜色拖影。涟漪只在创建时取一次 `Brush3` 快照。
- **不触碰 `Width`/`Height`/`Margin`/`Padding`**，不触发布局重算；软件渲染（Bazzite + llvmpipe）下每帧只是若干次属性赋值。
- 旧版 `Styles/Controls.axaml` 里的样式级 `TransformOperationsTransition` 已**移除**：`RenderTransform` 由引擎独占，避免两套值互相覆盖；XAML 只保留 `:pointerover` / `:pressed` 的颜色态。
- 同时 Track 上限 140，超限直接跳终态；涟漪同屏上限 15 个；`FxLayer` 全层 `IsHitTestVisible=False` + `ClipToBounds`。
- 与 `CampfireLogo`（10fps 帧动画）、主题 400ms 插值互不干扰。

### 开关

| 环境变量 | 效果 |
|---|---|
| `CAMPFIRE_ANIM=0` | 全部关闭：入场、呼吸、涟漪、脉冲、摇曳、营火帧动画都静止，界面功能完整可用 |
| `CAMPFIRE_ANIM=1` | 强制开启（覆盖系统「减少动画」设置） |
| 未设置（Linux） | 尊重 GNOME `org.gnome.desktop.interface enable-animations`；无 gsettings 则默认开 |
| `CAMPFIRE_ANIM_FPS=15..60` | 调整共享 timer 帧率，默认 30 |

关闭时 `AttachTypewriter` 仍订阅 `Session.Code` 并直接显示全文，邀请码不会丢失。

验证：三工程 `0 警告 0 错误`、selftest 11 项全过；Windows 真启动 GUI 抓取入场中间帧、稳定帧、涟漪帧、色板切换后的稳定帧，以及 `CAMPFIRE_ANIM=0` 模式截图 —— 稳定帧中主页与设置页卡片全部完整清晰、无半透明残留、无错位、无光斑残留。

## 界面本地化（2026-11-26 新增）

面向 GitHub 发布，界面与 `linux/` 文档都提供 **简体中文 / 繁體中文 / English** 三语。

| 层 | 做法 |
|---|---|
| 界面文案 | `Campfire.Avalonia/Localization/Loc.cs` + `Strings.cs`（188 键 × 3 语）。XAML 统一写 `{Binding L[Key]}`，ViewModel 动态文案用 `Loc.Get` / `Loc.Format` |
| 语言按钮 | 标题栏右上角还原原版「繁」圆按钮的位置，改成 **简 → 繁 → EN** 循环，鼠标悬停显示提示 |
| 持久化 | `settings.ini` 的 `ui.language`（`zh-Hans` / `zh-Hant` / `en`）；未设置时跟随 `CultureInfo.CurrentUICulture`（zh-Hant → 繁體，非 zh → English） |
| 即时切换 | `LocSource` 索引器发 `Item[]` 通知刷新静态文案；`ViewModelBase` 通过弱引用订阅 `RaiseAll()` 刷新带参数的动态文案；子页标题存本地化键而不是译文 |
| 文档 | `README.md`（简体）/ `README.zh-Hant.md`（繁體）/ `README.en.md`（English），顶部互相链接 |

设计对齐上游 `src/Campfire/Localization/Text.cs`：纯 C# 词表 + `LanguageChanged` 事件 + 索引器绑定。上游的 `TextExtension.cs` 依赖 `System.Windows.Data` / `Markup`（WPF），Avalonia 用不了，因此 Linux 侧另写等价的 `LocSource`；术语沿用上游四语词表的译法。上游另有「粵語」，本分支按需求只做三语。

**给翻译贡献者的提示**：所有可见文案集中在 `Strings.cs` 的 `Chinese` / `TraditionalChinese` / `English` 三个 `Table(...)` 里，键集合必须完全一致；缺译时 `Loc.Get` 退回简体，再缺就回显键名（便于发现漏翻）。`Loc.Format` 的 `{0}` 占位符在三种语言里都要保留且顺序一致。

## 分发与安装（面向最终用户，2026-11-26 新增）

`campfire-linux.tar.gz` 是**源码包**（给部署与编译用）；这一节说的是**成品安装包**：普通用户从 GitHub Release 下载后直接装，不需要 .NET SDK、不需要编译、不需要 root。

### 提供哪几种格式

| 格式 | Release 文件名 | 用户下载后 | 说明 |
|---|---|---|---|
| **AppImage**（推荐） | `Campfire-1.1.32-x86_64.AppImage` | `chmod +x` 后双击运行 | 单文件、跨发行版、无沙箱，EasyTier 能正常建立虚拟网卡 |
| **tar.gz 用户级安装** | `campfire-1.1.32-linux-x64.tar.gz` | 解压后 `bash install.sh` | 带桌面图标 / 应用菜单 / `campfire` 命令 / `uninstall.sh` |
| Flatpak（实验性） | `release/flatpak/campfire.yml.in` | 自行用 `flatpak-builder` 构建 | ⚠ 沙箱不授予 `/dev/net/tun`，联机很可能不可用；详见清单注释 |

**不提供 `.deb` / `.rpm`**：Bazzite 是 rpm-ostree 不可变系统，`rpm-ostree install` 会改镜像层并需要重启；Debian 系包在此完全不适用。前两种格式只写 `$HOME`，不改动系统。

### 用户怎么装

AppImage（把 OWNER/REPO 换成实际仓库，或直接在 Release 页面点文件名下载）：

```bash
curl -LO https://github.com/OWNER/REPO/releases/download/v1.1.32/Campfire-1.1.32-x86_64.AppImage
chmod +x Campfire-1.1.32-x86_64.AppImage
./Campfire-1.1.32-x86_64.AppImage
```

tar.gz 用户级安装：

```bash
curl -LO https://github.com/OWNER/REPO/releases/download/v1.1.32/campfire-1.1.32-linux-x64.tar.gz
tar -xzf campfire-1.1.32-linux-x64.tar.gz
cd campfire-1.1.32-linux-x64
bash install.sh
```

装完在应用菜单找 Campfire，或运行 `~/.local/bin/campfire`。`install.sh` 只写 `~/.local`，不需要 sudo，Bazzite / Fedora Silverblue 这类不可变系统可直接用。卸载用 `bash uninstall.sh`，设置、日志与 EasyTier 组件会保留。下载校验：Release 里有 `SHA256SUMS`，用 `sha256sum -c SHA256SUMS`。

### 自己怎么构建发布包

**Windows 开发机上就能做**（.NET 8 SDK 支持交叉编译 linux-x64，不需要 WSL / Docker）：

```bash
cd linux
bash pack-release.sh
```

产物：`linux/dist/campfire-1.1.32-linux-x64.tar.gz`（约 64 MB，含自包含运行时与 EasyTier 离线包）。设 `INCLUDE_EASYTIER=0` 可出更小的包，首次联机时应用会自动下载组件。脚本会做 ELF 头校验、Unix 权限位写入、AppStream 占位符替换、禁用内容扫描和 LF 校验和。

**AppImage 必须在 Linux 上构建**（需要 squashfs / `appimagetool`）。仓库里的 `.github/workflows/release.yml` 会在 `ubuntu-22.04` runner 上完成：push 一个 `v1.1.32` 标签，或在 Actions 里手动触发，CI 就自动产出 AppImage、两种 tar.gz 与 `SHA256SUMS` 并挂到 Release —— 用户即可从 GitHub 直接下载。单独构建：

```bash
bash release/build-appimage.sh dist/campfire-1.1.32-linux-x64.tar.gz Campfire-1.1.32-x86_64.AppImage
```

### 合规边界

- **绝不含** Minecraft 客户端、Mojang 资源、Temurin JDK（版权与体积原因，见 `THIRD-PARTY-NOTICES.md`）。游戏与 JDK 由用户自行安装。
- 内置 EasyTier 2.6.4 时按 **LGPL-3.0** 分发**未修改的独立程序**，随包附 `docs/LGPL-3.0.txt`；应用通过 `CAMPFIRE_EASYTIER_ZIP` 在首次联机时离线解压，不必联网。
- 本移植整体为 GPL-3.0-or-later，包内附 `LICENSE`、`THIRD-PARTY-NOTICES.md` 与 `docs/SOURCE.txt`；**完整对应源码** `campfire-linux.tar.gz`（含 `src/java-agent/` 全部源码与 40 个 Mappings 名字表）会随每个 Release 一同发布。

### 已知限制

- 目前只有 **linux-x64**；arm64 需把 `-r` 改成 `linux-arm64` 并另备 aarch64 版 EasyTier。
- Windows 侧交叉编译出的 ELF **没有 Unix 执行位**，`install.sh` / `build-appimage.sh` 会 `chmod +x` 补上（打包脚本用 Python tarfile 精确写入 0755）。
- AppImage 需要 FUSE（Bazzite 默认具备）；没有 FUSE 时加 `--appimage-extract-and-run`。
- Flatpak 的联机限制见上表，AppImage 才是推荐格式。

## 关键约束

- 不碰 `../src/android/`。
- `RoomCode` / `RoomProtocol` / `RoomFraming` / `EasyTierConfig` 等协议文件**只用链接、禁止副本与编辑**，否则官方 Windows 客户端会不认。
- 大厅 ≠ 联机：邀请码靠 EasyTier 公共节点发现 + 点对点，游戏数据不经过任何服务端；砍掉大厅照样联机。
- Java Agent 已于 2026-11-19 恢复（跨平台 JVM attach）；CLI 仍需手填端口，GUI 正式联机入口可自动开放，但需完整 JDK 与真实 MC。
- 不用 Wine / Mono 跑原 exe（WPF 在 Linux 无运行时）。
- 原 Windows 工程零改动，作为对照与回退基线。

## GPL-3.0 合规

- 每个新写/改写文件头部都标注了「修改自 / 基于 Xocio/Campfire v1.1.32 (GPL-3.0)」及修改内容；被 `<Compile Include>` 链接、逐字未改的文件不加声明。
- 分发二进制须同时提供本 fork 的**完整对应源码**（含 `Campfire.Cli/Link/LinkSession.cs` 改写副本、`Campfire.Lobby/` 全部新增文件与所有被链接的原 `Link\` 文件），并保留 `../LICENSE` 与 `../THIRD-PARTY-NOTICES.md`。
- EasyTier（LGPL-3.0）保持独立进程、未修改、按需下载；dnlib（MIT）、ASM（BSD-3-Clause）随原工程的第三方声明。自建大厅服务端不引入新的第三方 NuGet 依赖。
