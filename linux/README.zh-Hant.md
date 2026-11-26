<p align="center">
  <a href="README.md">简体中文</a> | <b>繁體中文</b> | <a href="README.en.md">English</a>
</p>

# Campfire Linux 移植

基於 `Xocio/Campfire v1.1.32`，GPL-3.0。原 Windows 工程 `../src/Campfire/` **全程零改動**，只作爲原始碼來源與對照基線；`linux/` 全是新增檔案，刪掉即可完全回退。

> 這是一個社區移植分支，不是官方版本；不連接原作者的大廳服務。

## 當前進度：階段 1 / 階段 2 / 26.3 端到端 全部完成 ✅

| 目錄 | 狀態 |
|---|---|
| `Campfire.Cli/` | ✅ 完整 `Link/` 層 + `LinkSession` 編排層 + Linux 適配層 + CLI 會話層 + 大廳用戶端；`selftest` **11 項全過** |
| `Campfire.Lobby/` | ✅ ASP.NET Core Minimal API 自建大廳，`verify-lobby.sh` **42 項全過**（詳見 `Campfire.Lobby/README.md`） |
| `Campfire.Avalonia/` | ✅ **兩批全部交付**：Avalonia 11 編譯 0 警告 0 錯誤；4 Tab + 完整動態主題（OKLCH / 9 種子色 / 6 風格 / 400ms 過渡）+ 地圖只讀掃描 / 工具真功能 + 13 項介面動效 + **簡 / 繁 / EN 三語介面** |

### 階段 1 驗收結果（2026-11-18，Bazzite VM × 官方 Windows `Campfire.exe` v1.1.32 實測）

| 方向 | 房主 | 加入方 | 邀請碼 | 結果 |
|---|---|---|---|---|
| A | Linux CLI（`--skip-probe`） | 官方 Windows 用戶端 | `U/K3LB-EP2X-V5HG-TGM9` | ✅ `階段=Ready 玩家=2 直連=1`；Windows 端直連·TCP 0.7 ms、NAT4 |
| B | 官方 Windows 用戶端 + `windows-test/` 假世界樁（遊戲連接埠 25565） | Linux CLI（`--skip-probe`） | `U/LBFC-QVX9-GYL7-EZH3` | ✅ `階段=Ready 玩家=2`；Windows 端直連 1 · 中轉 0、0.5 ms |

兩個方向都通 → **階段 1 硬標準達成**。免剪貼簿腳本：`step2.sh`（裝元件）、`step3.sh`（迴環自檢）、`step4.sh`（雙向互通）。

> **邊界，別當成"能連線打遊戲了"**：兩端都沒裝真實 MC、全程 `--skip-probe`，只驗「邀請碼 → RoomCode → EasyTier 組網 → 直連 TCP → 房間協定 → 玩家列表」。這是階段 1 當時的邊界：實際進遊戲世界、模組同步、語音、官方大廳均未驗證（後兩項本就裁剪）；**2026-11-25 已用真實 MC 26.3 + 自動開放區域網路補齊「進遊戲世界」這一項**，詳見「Minecraft 26.3 端到端實測」一節，其餘仍屬裁剪。玩家列表裏 `Campfire 1.0.0` 只是 Linux CLI 程式集版本顯示，房間協定無版本校驗；監控裏偶發 `直連=0 / 節點=-1` 是 EasyTier RPC 快照滯後，以對端顯示為準。

### 工程結構

| 路徑 | 作用 |
|---|---|
| `Campfire.Cli/Campfire.Cli.csproj` | `net8.0` 控制臺工程。用**共享原始碼鏈接**把 `../../src/Campfire/Link/**/*.cs` 逐字編進來（禁止副本與編輯，防協定漂移）；剔除 10 個 Windows 專屬檔案 |
| `Campfire.Cli/Link/LinkSession.cs` | **唯一物理複製並改寫的原檔案**（1743 行編排層）。僅新增 `SkipProbe` 開關（4 處探測短路），其餘逐字不變 |
| `Campfire.Cli/Cli/` | 命令層：`SelfTest`、`VersionCommand`、`Doctor`、`ConfigCommand`、`EasyTierCommand`、`LobbyCommand`、`SessionCommands`、`SessionRuntime`（消息泵 / 狀態快照 / `CliHost`） |
| `Campfire.Cli/Linux/` | **真正的 Linux 適配層**：`EasyTierPackage`（Linux 發行包 + chmod）、`ChildProcessJob`（退出鉤子替代 Job Object）、`GameAccount`、`LocalWorldFinder`（`/proc`）、`LauncherProcess`、`AppSettings`（XDG）、`LinuxConfig` |
| `Campfire.Cli/Shims/` | 被裁剪功能的**安全空實現**：`Text`、`Mods`、`Worlds`、`OnlineService`、`Telemetry`、`MinecraftStubs`（JavaAgent / AgentState / LanOpener） |
| `Campfire.Lobby/` | 自建大廳伺服器端：`Program.cs`（Minimal API 端點）、`LobbyStore.cs`（記憶體字典 + 心跳過期 + 密碼/IP 摘要）、`LobbyOptions.cs`、`Contracts.cs`、`LobbyCleanupService.cs`、`appsettings.json` |
| `build.sh` / `run-lobby.sh` / `verify-lobby.sh` | 一鍵建置兩個工程 / 啓動大廳 / 42 項契約驗收 |
| `fetch-easytier.sh` | EasyTier 2.6.4 Linux 元件斷點續傳下載 |

**關鍵取捨**：原計劃想「大段刪掉 700 行裁剪功能」，實際改成 **給一套最小 shim 讓 `LinkSession.cs` 幾乎逐字不改地編譯**。好處是協定常量、房間編排、EasyTier 調用鏈完全不動，最大程度避免與官方 Windows 用戶端協定漂移；被裁剪的功能由 shim 恆返回「未生效」的安全值靜默禁用。伺服器端同樣鏈接原 `RoomCode.cs` 做邀請碼校驗，防止下發用戶端解析不了的碼。

被剔除的 10 個原檔案：`LinkSession.cs`、`Diagnostics.cs`、`Minecraft/{GameAccount,LocalWorldFinder,JavaAgent,LanOpener,PlayerHeads,GameMappings}.cs`、`Runtime/{EasyTierPackage,ChildProcessJob}.cs`。其中 4 個由 `Linux/` 同名類替代，其餘按計劃刪除或由 shim 承接。

## 搬到 Linux 機器上（部署）

### 最容易踩的坑：只拷 `linux/` 一定編譯失敗

兩個 `.csproj` 都用 `$(MSBuildThisFileDirectory)../../src/Campfire/` **共享原始碼鏈接**復用原協定層（協定檔案禁止複製、禁止改寫，防止與官方用戶端協定漂移）。所以 Linux 上必須保持這個同級結構：

```
~/campfire-linux/                      ← 包根，路徑別帶空格 / 中文
├── bootstrap.sh
├── LICENSE  THIRD-PARTY-NOTICES.md
├── src/Campfire/
│   ├── Hex.cs
│   └── Link/**/*.cs                   ← 41 個原協定層檔案，逐字未改
└── linux/
    ├── Campfire.Cli/
    ├── Campfire.Lobby/
    ├── build.sh / run-lobby.sh / verify-lobby.sh / fetch-easytier.sh
    └── README.md
```

> ⚠️ 你現在 Windows 上的目錄叫 `camp fire linux移植計劃`，**既有空格又有中文**。腳本雖然都加了引號，但 Linux 上強烈建議一律解壓到 `~/campfire-linux`，少踩一堆路徑坑。

### 方案 A：一個包全帶走（推薦，最省事）

在本機 Git Bash 裏生成自包含原始碼包：

```bash
cd "/c/Users/yzhhh/Desktop/camp fire linux移植計劃/Campfire-1.1.32/Campfire-1.1.32/linux"
bash pack-for-linux.sh
```

產出 `campfire-linux.tar.gz` + `.sha256`。包裏已含原協定層、GPL 合規檔案、全部新代碼與腳本；`.sh` 統一轉成 LF 並加了可執行位（CRLF 的腳本在 Linux 會報 `bad interpreter`）。改過代碼後重跑一次即可重新打包。

**把包弄到 Linux**（挑一種）：

| 方式 | 怎麼做 |
|---|---|
| WSL2（最省，連打包都不用） | `cp -r "/mnt/c/Users/yzhhh/Desktop/camp fire linux移植計劃/Campfire-1.1.32/Campfire-1.1.32" ~/campfire-linux`。WSL 能直接讀 C 盤；**但 Windows 檔案系統上編譯很慢，務必先 cp 到 `~/` 下的 ext4** |
| U 盤 / 虛擬機器共享檔案夾 | 把 `campfire-linux.tar.gz` 拷過去，完全不依賴網路，學校網慢時首選 |
| 同一區域網路 `scp` | Linux 上先 `sudo apt install -y openssh-server`，Windows 側 `scp campfire-linux.tar.gz 用戶名@Linux的IP:~/` |

### 方案 B：只傳 `linux/`（傳輸量最小，約 200 KB）

Linux 機器能上網時，在那邊重新下官方原始碼，只把我們的 `linux/` 目錄拷過去：

```bash
mkdir -p ~/campfire-linux && cd ~/campfire-linux
wget -c https://github.com/Xocio/Campfire/archive/refs/tags/v1.1.32.tar.gz -O src.tar.gz
tar -xzf src.tar.gz --strip-components=1        # 得到 src/Campfire/... 等
# 再把 Windows 上的 linux/ 整個目錄拷進來（U 盤 / scp）
cp -r /path/to/linux ./linux
```

學校網慢就用迅雷在 Windows 上下 `https://github.com/Xocio/Campfire/archive/refs/tags/v1.1.32.zip`，解壓後拷過去，內容完全一樣。

### 方案 C：VMware 虛擬機器拖不進去時（Bazzite / 不可變發行版）

**爲什麼會「毫無反應」**：VMware 的拖放、剪貼簿共享、共享檔案夾都不是虛擬機器自帶功能，要靠客戶機裏 `open-vm-tools-desktop` 的 `vmware-user` 進程。Bazzite 基於 Fedora Atomic（Kinoite / Silverblue），`/usr` 只讀、面向裸機遊戲設備，預設不裝這套工具；而且它預設跑 **Wayland**，而 VMware 拖放走的是 X11 Xdnd 協定，Wayland 下本來就是壞的（open-vm-tools 官方 issue #792 正在跟蹤，其中就包含基於 Bazzite 的鏡像）。所以檔案**根本沒進虛擬機器**，不是包壞了。

另一點：`.tar.gz` **不是可執行程式**，雙擊本來就不會「彈出安裝介面」，要用終端 `tar -xzf` 解開；Bazzite 若沒裝歸檔管理器，雙擊確實什麼都不發生。另外 VMware 拖放對**整個檔案夾**支援很差，只適合拖單個檔案。

**第 0 步：先確認檔案到底有沒有進去**（Bazzite 裏開終端）：

```bash
ls -la ~/Downloads ~/Desktop 2>/dev/null
find ~ -maxdepth 3 -name 'campfire-linux*' 2>/dev/null
```

找得到 → 直接跳到下面「解壓 + 初始化」，不用再傳。找不到 → 用下面的 HTTP 辦法。

**不依賴 VMware Tools 的傳法：臨時 HTTP 服務**。已寫好 `linux/serve-for-vm.py`（只讀、不改系統、Ctrl+C 即消失，本機實測下載後字節完全一致）：

Windows 主機上執行（Git Bash 或終端）：

```bash
python "C:/Users/yzhhh/Desktop/camp fire linux移植計劃/Campfire-1.1.32/Campfire-1.1.32/linux/serve-for-vm.py"
```

腳本會打印本機所有 IPv4 和完整下載命令（**一律以它打印的為準**）。

> ⚠️ **別照抄聊天或舊文檔裏的示例 IP**。本機 WLAN 實際是 `172.16.202.127`（校園網 JunCheng-Teacher 4，DHCP 會變）；早期寫過的 `192.168.1.5` 只是示例，這臺機器上根本不存在，照抄必然卡 21 秒然後 `curl: (7) Failed to connect`。

防火牆：WLAN 目前是「公用網路」，已有 `python.exe` 入站放行規則；首次彈窗勾「允許訪問」，然後**讓這個視窗一直開着**（關掉服務就沒了）。

Bazzite 虛擬機器裏按順序做：

```bash
cd ~

# 1) 先看自己有沒有 IP，以及 NAT 模式下的主機網關地址
ip -4 addr show
ip route | awk '/default/{print $3}'

# 2) 填主機地址：NAT 用上面 ip route 的輸出；橋接用主機 WLAN 地址
HOST_IP=172.16.202.127

# 3) 下載 + 校驗（看到 campfire-linux.tar.gz: OK 才算成功）
curl -f -O --max-time 30 http://$HOST_IP:8000/campfire-linux.tar.gz
curl -f -O --max-time 30 http://$HOST_IP:8000/campfire-linux.tar.gz.sha256
sha256sum -c campfire-linux.tar.gz.sha256
```

懶得填 IP，就一鍵把常見地址全試一遍：

```bash
for ip in $(ip route | awk '/default/{print $3}') 172.16.202.127 192.168.1.1 192.168.137.1 192.168.220.1; do
  if curl -fsS --max-time 5 -o /dev/null "http://$ip:8000/"; then echo "通了: $ip"; echo "$ip" > ~/cf-host-ip; break; fi
done
HOST_IP=$(cat ~/cf-host-ip)
```

連不上按順序查：

1. 虛擬機器裏 `ping -c 3 $HOST_IP`，不通就是網路層問題，往下走。
2. 主機自測 `curl -s -o /dev/null -w "%{http_code}" http://172.16.202.127:8000/`，應返回 `200`。不是 200 說明服務沒起，重跑 `serve-for-vm.py`。
3. 管理員 PowerShell 裏放行連接埠（自測 200 但虛擬機器超時，多半是它）：
   ```powershell
   New-NetFirewallRule -DisplayName "Campfire HTTP 8000" -Direction Inbound -Protocol TCP -LocalPort 8000 -Action Allow -Profile Any
   ```
4. **本機 `VMnet1` / `VMnet8` 虛擬網卡當前是 Down / Not Present** —— NAT 和「僅主機」模式可能壓根沒網。VMware 菜單「編輯 → 虛擬網路編輯器 → 還原預設設定」（要管理員）重建虛擬網卡；或把虛擬機器網路改成**橋接模式**並勾「複製物理網路連接狀態」。
5. 校園 Wi-Fi 若開了 AP 用戶端隔離，橋接的虛擬機器也 ping 不通主機 → 換手機熱點，或直接用 U 盤。
6. 還不行就換連接埠：`CF_PORT=8001 python ".../linux/serve-for-vm.py"`，虛擬機器裏把 `:8000` 改成 `:8001`。

> 兜底：不想折騰網路就用 U 盤（VMware 菜單「虛擬機器 → 可移動設備 → 連接 USB」，Bazzite 會掛到 `/run/media/$USER/`）。共享檔案夾要 `rpm-ostree install open-vm-tools` 改基礎層並重啓，不推薦。
>
> 校驗檔案缺失時不要慌：那只是上一條 `curl` 失敗沒下到本地，不是包壞了。先在瀏覽器打開 `http://<主機IP>:8000/` 看目錄裏有沒有兩個檔案。

**Bazzite 上跑 bootstrap 的注意事項**：

| 事項 | 說明 |
|---|---|
| 別用 `dnf install` | `/usr` 只讀，裝系統包要靠 `rpm-ostree`（需重啓）。本方案**完全不碰系統層** |
| .NET 8 SDK | 用 `bash bootstrap.sh --install-sdk`，裝到 `~/.dotnet`（`$HOME` 可寫），不需要 sudo |
| 必備命令 | `tar` / `curl` / `sha256sum` / `bash` 都在基礎鏡像裏，無需額外安裝 |
| 資料目錄 | 預設 `~/.local/share/campfire-linux/`，在 `$HOME` 下，可寫 |
| 執行方式 | 顯式寫 `bash bootstrap.sh`；Wayland / X11 無所謂，全程在終端裏做 |
| 若 .NET 報缺 `libicu` / `libssl` | 退路是用 Bazzite 自帶的 Distrobox：`distrobox create -n cf -i ubuntu:24.04 && distrobox enter cf`，在容器裏跑 `bootstrap.sh` |

### 在 Linux 上解壓 + 初始化

```bash
cd ~
sha256sum -c campfire-linux.tar.gz.sha256     # 可選：確認傳輸沒損壞
tar -xzf campfire-linux.tar.gz                # 得到 ~/campfire-linux/
cd campfire-linux
bash bootstrap.sh                            # 檢查 SDK → 修權限 → 建置兩個工程 → 11 項自檢 + doctor
```

沒裝 .NET 8 SDK 時 `bootstrap.sh` 會打印三條安裝命令並停下；也可以一步到位（裝到 `~/.dotnet`，不需要 sudo，支援斷點續傳）：

```bash
bash bootstrap.sh --install-sdk
```

成功的標誌：末行 `SELFTEST OK`，`doctor` 在 Linux 上除「EasyTier 元件未安裝」外全綠。腳本還會打印一個 `alias campfire=...`，建議照抄執行，後面命令就不用寫一長串路徑。

### 手動建置（不跑 bootstrap 時）

```bash
cd ~/campfire-linux/linux/Campfire.Cli
dotnet build -c Release
dotnet run -c Release --no-build -- selftest
dotnet run -c Release --no-build -- doctor
```

`build.sh` 同時編譯 `Campfire.Cli` 與 `Campfire.Lobby`（都用 .NET 8 共享框架，無額外 NuGet 依賴）。

### 還要單獨下 EasyTier（約 25 MB）

**原始碼包裏不含 EasyTier 二進制**（LGPL-3.0，按需下載，且必須與官方 Windows 用戶端同爲 2.6.4）。到 Linux 上後執行 `bash linux/fetch-easytier.sh`，詳見下文「EasyTier 2.6.4 Linux 元件」一節。學校網慢可用迅雷在 Windows 下好 zip 再拷過去，用 `campfire easytier install --local <zip>` 安裝。

## CLI 命令

```bash
campfire --help
campfire selftest                       # 11 項斷言，末行 SELFTEST OK
campfire version
campfire doctor                         # 平臺 / 資料目錄 / 元件 / 服務地址 / 編譯內容
campfire config path|list|get <鍵>|set <鍵> <值>|unset <鍵>
campfire easytier status|install [--local <zip>]|check|uninstall
campfire lobby status|list|publish|join  # 自建大廳，需 --service-url
campfire code                           # 生成一個合法邀請碼
campfire code --check <邀請碼>          # 校驗並展開 network_name / network_secret

# 連線會話（須在 Linux 上）
campfire host --port 43210 --player 小明 --skip-probe
campfire join --code U/XXXX-XXXX-XXXX-XXXX --player 小紅 --skip-probe
campfire status | peers | logs --lines 100 | stop
```

CLI 的 `host` 需要 `--port`：命令行模式不帶遊戲內注入，需先在 Minecraft 裏點「對區域網路開放」，再把連接埠傳給程式。**GUI 的正式連線入口**已恢復 Java Agent 自動開放（見後文「自動開放區域網路世界」章節），建房頁勾上後不用手填連接埠。
`join` 成功後會打印本地連接埠，Minecraft 多人遊戲裡連 `127.0.0.1:<本地連接埠>`。

可用參數：`--player`、`--local-port`、`--node`（自定義節點）、`--hide-ip`、`--no-relay`、`--share-lan`、`--timeout 30|60|120`、`--skip-probe`。

全局選項（可放在命令前後）：

| 選項 | 作用 |
|---|---|
| `--data-dir <目錄>` | 覆蓋 XDG 資料目錄，便於同機跑兩個實例做迴環測試 |
| `--service-url <地址>` | 自建大廳地址；只接受 https 或指向本機的 http（對齊官方校驗規則） |

> `--skip-probe` 跳過 `MinecraftProbe` 的 4 處世界探測，讓沒有真實 MC 世界時也能做迴環連線自檢。正式連線**不要**加，否則不會校驗連接埠確實是遊戲伺服器。

### 會話模型（重要）

`host` / `join` 是**前臺長駐進程**，保持執行房間才存在。CLI 是「一個命令一個進程」，所以長駐進程周期性把狀態寫到 `<資料目錄>/session.json`，`status` / `peers` 讀它，`stop` 寫 `stop.requested`、前臺進程在 1 秒內檢測到並釋放房間。

因此 **`host`/`join` 與查詢命令必須使用同一個 `--data-dir`**。迴環測試時開兩個終端、各用不同的 `--data-dir`，但查詢時要帶上對應那一個。

`logs` 讀 `<資料目錄>/Link/link.log`（原 `LinkLog` 路徑，超 2 MB 自動回滾），裡面也有 easytier-core 最近的輸出。

資料目錄預設 `~/.local/share/campfire-linux/`（遵循 `XDG_DATA_HOME`），配置存 `settings.ini`。`host`/`join` 啓動時會把已裁剪項顯式關掉並寫入配置：`AutoOpenLan`、`BackupWorld`、`Voice`、`ShareMods`、`ServeMods`、`AutoCopyCode`、`DetectClipboard`。

## 自建連線大廳（階段 1.5）

作者只開源了 REST 用戶端 `LobbyClient.cs`，`api.mccampfire.top` 的伺服器端沒公開，所以「保留公開房間列表」只能自建。伺服器端契約、錯誤碼錶、配置與隱私約定全部寫在 **`Campfire.Lobby/README.md`**。

```bash
cd ~/campfire-linux/linux
./run-lobby.sh                 # 預設 http://127.0.0.1:5080，只監聽本機
# 另開終端：
./verify-lobby.sh              # 42 項 curl 斷言 → LOBBY VERIFY OK

campfire --service-url http://127.0.0.1:5080 lobby list
campfire --service-url http://127.0.0.1:5080 lobby publish \
    --code U/XXXX-XXXX-XXXX-XXXX --title '我的房間' [--password 1234] [--once]
campfire --service-url http://127.0.0.1:5080 lobby join <房間ID> [--password 1234]
```

`lobby publish` 預設前臺長駐並按伺服器端給的心跳間隔續約，Ctrl+C 撤下；`--once` 發布後立即撤下，用於自檢。它**不建立連線房間**，邀請碼必須來自另一個正在 `host` 的用戶端。

**三條邊界**（最容易誤解）：

1. **自建大廳與官方大廳互不可見**：官方 exe 編譯時把 `ServiceUrl` 燒進程式集，指向 `api.mccampfire.top`；只有指向本服務的建置能看到這裡的房間。
2. **不影響邀請碼互通**：邀請碼走 EasyTier 公共節點 + 點對點，整條鏈路不調用 `OnlineService`，Linux CLI ⇄ 官方 Windows 用戶端照常互進。
3. **不要連官方服務**：原始碼建置不連在線服務是作者明確的設計意圖；自建伺服器端對 `/v1/manifest` 等一律返回 404 靜默降級。

## EasyTier 2.6.4 Linux 元件

版本**必須鎖 2.6.4**，與官方 Windows 用戶端一致，否則虛擬網路協定不相容。已核對 GitHub Release v2.6.4 的實際資產名：

| 架構 | 資產名 | 大小 |
|---|---|---|
| x86_64 | `easytier-linux-x86_64-v2.6.4.zip` | 25.5 MB |
| aarch64 | `easytier-linux-aarch64-v2.6.4.zip` | 20.5 MB |

```bash
# 斷點續傳下載（多個源自動切換，含國內鏡像）
bash fetch-easytier.sh
# 然後裝好
campfire easytier install --local ./easytier-linux-x86_64-v2.6.4.zip
campfire easytier check
```

也可以讓程式自己下載：`campfire easytier install`。學校網慢時推薦先用下載工具（迅雷/aria2）下好 zip，再用 `--local` 或環境變量 `CAMPFIRE_EASYTIER_ZIP` 指定。

> **校驗和說明（重要）**：官方 v2.6.4 Release **沒有提供 Linux 資產的校驗和檔案**（Windows 版原始碼裏硬編碼了三個檔案的 SHA-256，Linux 版沒有對應資料）。本分支**不編造哈希**：`InstallAsync` 解壓後會自動 `chmod +x`，若你在安裝目錄放了 `checksums.sha256`（每行 `<hex>  檔案名`）就逐項比對，否則記一條警告。需要嚴格校驗時請自行 `sha256sum` 核對。
>
> `EasyTierConfig.cs` 保持 `no_tun = true`、`private_mode = true`，因此**不需要 root、TUN/TAP、驅動、防火牆規則**；Linux 分支 `IsWindows7 = false`，會啓用 QUIC 分支。

## 本機驗證結果（Windows 開發機，.NET 8.0.428 SDK 在 `C:\Users\yzhhh\.dotnet`）

階段 1c（2026-11-14）：

```
dotnet build -c Release   →  0 警告 0 錯誤（含 LinkSession 本地副本與全部 shim）
campfire selftest         →  11 項全過，SELFTEST OK
campfire code / --check   →  生成 U/4KUY-3B4J-TUQ6-RD9Y，展開
                             network_name=scaffolding-mc-4KUY-3B4J / secret=TUQ6-RD9Y
campfire host / join      →  參數校驗正確；在 Windows 上明確拒絕啓動會話並退出 2
campfire status/peers/logs/stop/config/version/doctor → 全部通過
```

第 11 項自檢會**實際 `new LinkSession(pump)`**（只構造、不聯網），並驗證替代 WPF `DispatcherSynchronizationContext` 的消息泵真的能 `Post` 入隊、`PumpAll` 出隊執行——防止「編譯通過但編排層沒被鏈進來」的假象。

階段 1.5（2026-11-15，新增）：

```
兩個工程 dotnet build -c Release  →  0 警告 0 錯誤
./verify-lobby.sh                 →  42 項全過，0 失敗 0 跳過，LOBBY VERIFY OK
配置觸發的 429 / 503              →  臨時實例（5081 / 5082）驗證通過
campfire lobby list               →  用官方 LobbyClient 解析器讀到自建伺服器端房間
campfire lobby publish --once     →  POST + DELETE 全鏈路通過
campfire lobby join               →  密碼校驗與換碼通過
```

> Windows 上只能編譯、自檢與跑 HTTP 伺服器端；`host` / `join`、EasyTier 二進制執行、與官方 Windows 用戶端互通，必須在 Linux 上做。程式在非 Linux 平臺會明確拒絕啓動會話，而不是卡在下載元件上。

## 免剪貼簿工作流（Bazzite / VMware 必看）

VMware 的拖放、共享檔案夾、**剪貼簿共享**都依賴客戶機 `open-vm-tools-desktop` 的 `vmware-user`；Bazzite 是不可變系統 + Wayland，預設沒有這套東西。主機複製的新命令根本同步不進虛擬機器，粘貼只會得到緩存的舊命令（表現爲「一粘貼就變成最早那條裝 SDK 的命令」）。因此每一步都做成「HTTP 目錄裏的腳本 + 一行 curl」，只需手敲一行：

> ⚠️ **`<主機IP>` / `<IP>` 是佔位符，敲命令時要連尖括號一起換成服務視窗打印的數字**（例如 `192.168.1.5`）。
> bash 裏 `<` / `>` 是重定向符號，照抄尖括號會報 `bash: 192.168.1.5: 沒有那個檔案或目錄`——
> 注意報錯前綴是 **`bash:` 而不是 `curl:`**，說明命令在 shell 解析階段就被拆了，curl 根本沒啓動，
> 跟網路、服務、腳本都無關。在 IP 中間加空格只會讓報錯變成 `bash: 192.: 沒有那個檔案或目錄`。
> 另外命令裏儘量別出現 `-` 開頭的選項組合：手敲漏橫槓會把 `fss` 當成主機名。

| 步驟 | 一行命令 | 腳本做了什麼 |
|---|---|---|
| ② 裝 EasyTier | `curl 192.168.1.5:8000/step2.sh \| bash` | 找主機服務、檢查 SDK/編譯產物、寫 PATH 與 alias、下載並校驗 2.6.4、安裝、`check`、`doctor` |
| ③ 迴環自檢 | `curl 192.168.1.5:8000/step3.sh \| bash` | 自己起 host + join 兩個會話，從 `session.json` 讀邀請碼寫入 `~/cf-code.txt`，打印兩端 `status`/`peers`/日誌 + 明確 PASS/FAIL 結論後自動停止 |
| ③ 排障（只讀） | `curl 192.168.1.5:8000/step3-diag.sh \| bash` | 檢查 `easytier-core` 能否執行、公共節點 TCP、兩端 `Link/link.log` 錯誤行與尾部、殘留進程 |
| ④ 雙向互通（v2） | `curl 192.168.1.5:8000/step4.sh > step4.sh` 然後 `bash step4.sh`（兩條都要敲；**別用 `-fsS`，手敲漏橫槓會報 `Could not resolve host: fss`**） | 後臺跑 host/join + 日誌落盤 `~/cf-host.log` / `~/cf-guest.log`，每 3 秒打印階段與玩家數；會話意外退出時自動打印退出碼、錯誤行和日誌尾部 |
| ⑤ GUI 編譯與啓動 | `curl 192.168.1.5:8000/step5.sh \| bash` | 更新原始碼包、離線 NuGet（167 MB，已裝好就跳過）、離線 restore、三工程編譯 + 11 項自檢、字體/DISPLAY 檢查、啓動 GUI |
| ⑥ 裝完整 JDK | `curl 192.168.1.5:8000/step6.sh \| bash` | 下載並校驗 Temurin 17（185 MB）、解壓到 `~/.local/campfire-jdk`、驗證 `jdk.attach@`、寫 `campfire-jdk-env.sh` 並讓自動開放入口加載它 |

> 表裏 `192.168.1.5` 只是當前實測地址，**一律以服務視窗打印的「本機 IP」為準**（DHCP 會變）；照抄時不要帶 `<` `>`。所有腳本內部也會自動探測網關與歷史地址，可用 `CF_HOST_IP=實際IP bash stepN.sh` 顯式指定。

兩個腳本放在 `linux/`（也就是 `serve-for-vm.py` 的 HTTP 根目錄），帶 GPL 修改聲明、LF 換行，`bash -n` 通過；只寫 `$HOME`，不改系統層、不需要 sudo。③ 加了 `--skip-probe`、沒有真實 MC 世界，只驗證「邀請碼 → EasyTier 組網 → 房間協定」這條鏈路；完整日誌留在 `~/cf-a.log`、`~/cf-b.log`。

**③ 通過的唯一判據**（不要只看 `status` 的某一瞬間快照，它每 2 秒才寫一次）：控制臺出現 `========== 已加入房間 ==========` 橫幅、`Found host scaffolding-mc-server-* (direct, tcp)`、`Host game port ... is reachable at local port ...`、`Joined room U/...`，以及 `[HH:MM:SS] 就緒 | ... 玩家 2`。`CliHost` 只在 `LinkStage.Ready` 時才打印該橫幅，所以有橫幅即通。

鏈路日誌裏幾種**無害噪音**，別誤判成失敗：

| 日誌 | 原因 |
|---|---|
| `WARN 官方 v2.6.4 Release 未提供 Linux 校驗和` | 上遊確實沒給 checksum；主機側已用 `sha256sum -c` 自行校驗過 |
| `WARN The running game was not found; the in-game overlay agent can not be attached` | `--skip-probe` + 虛擬機器沒裝 MC；遊戲內覆蓋層（Java Agent）本就在裁剪清單裏 |
| `Connection refused (os error 111)` / `failed to get manage client` / `tcp://127.0.0.1:<隨機連接埠>` | `easytier-core` 剛啓動時 RPC 管理連接埠還沒監聽，輪詢搶跑了幾拍；隨後連上才打印 `Found host`。統計列因此可能是 `節點 -1 / 直連 0`，但**實際網路是通的**（有 `direct, tcp` 與連接埠映射） |
| `Network instance stopped.` / `Room stopped by the user.` | `step3.sh` 結束時自動 `stop`，正常清理 |

> 正式與官方 Windows 用戶端互通時**不要**加 `--skip-probe`，且要先在 Minecraft 裏點「對區域網路開放」，再把連接埠傳給 `host --port`。只驗證房間層邀請碼互通時可以保留它。

### ④ 到底需不需要 Minecraft？分方向看（重要）

**方向 A（Linux 建房 → Windows 官方用戶端加入）：不需要，零下載。**

讀 `LinkSession.StartJoin`（原檔案 459–541 行）可知，加入方完全不查本地世界：裝元件 → 加入 EasyTier → `FindHostAsync` → 連接埠轉發 → 連 `RoomServer` → 發心跳 → 拉玩家列表 → `Ready`。唯一的探測是 `MinecraftProbe.IsServerAsync(localPort)`，探的是**通過隧道轉發到房主那邊的遊戲連接埠**，而且連不上也只是 `LinkLog.Warn(... continuing anyway)`（514–515 行），不阻斷。診斷頁那兩個灰點文案寫的是「**開房前**」「**房主**進入世界後」，都只約束建房，且是 `DiagState.Info` 而非 Fail（`Diagnostics.CheckGame` / `CheckWorld`）。

**方向 B（Windows 官方用戶端建房）：需要本機有一個「開放區域網路的世界」。**

`StartHost` 會 `await LocalWorldFinder.WaitForLocalWorldAsync(...)`，`MonitorHostAsync` 還會每 6 秒探一次世界連接埠，連續 3 次失敗就拋 `Link.Error.WorldClosed` 關房。沒有 MC 啓動器/用戶端時，用下面的驗證樁代替，不必下載幾百 MB。

### 零下載：`windows-test/` 假世界驗證樁

`LocalWorldFinder` 的判定只有三條（已逐條實測）：

| 條件 | 原始碼 | 驗證樁如何滿足 |
|---|---|---|
| 連接埠由**進程名以 `java` 開頭**的進程監聽 | `IsJava(pid)` → `ProcessName.StartsWith("java")` | 用 `C:\jdk-17\bin\java.exe` 啓動（本機已有 Temurin 17.0.20.1） |
| 發 `0xFE` 回 `0xFF` | `MinecraftProbe.IsServerAsync` | `FakeMcWorld.java` 回單字節 `0xFF` |
| 發 `0xFE 0x01` 回 1.4 版 legacy ping | `MinecraftProbe.GetStatusAsync` | 回 `0xFF + 2 字節 BE 長度 + UTF-16BE`，字段 `§1 / 47 / 1.20.1 / MOTD / 0 / 20` |

用法（Windows 本機，雙擊即可，**視窗要一直開着**）：

```
windows-test\start-fake-world.bat
```

它會自動編譯並啓動 `127.0.0.1:25565`。另開終端跑 `python windows-test\verify.py` 應看到兩行 `OK` + `PASS`；再到 Campfire 的「連線診斷」點「重新檢測」，「區域網路世界」會變綠並顯示連接埠 25565。「遊戲」那項仍是灰點（`CheckGame` 只看賬號，是 Info 級），不影響。

實測記錄：`IsServerAsync → reply=ff`；`GetStatusAsync → parts=['§1','47','1.20.1','Campfire Linux Port Test','0','20']`；`netstat` 顯示 `127.0.0.1:25565 LISTENING`，`tasklist` 顯示進程名 `java.exe`。

> **局限，別誤會**：這是協定樁，**不是能玩的 MC 伺服器**，沒人能真的進世界。階段 1 只驗「邀請碼 → EasyTier 組網 → 房間協定 → 玩家列表」這條鏈路，樁夠用；真要打遊戲還是得裝 MC，並且 Linux 端去掉 `--skip-probe`。

### 「等 10 秒就沒了」不是 Linux 連接埠的 bug

原始碼裏 `RoomServer.GuestTimeout = 10s`，`MonitorHostAsync` 每 2 秒調一次 `RemoveSilentGuests()`；加入方 `LinkSession` 注釋寫得很直白：`// Heartbeat every few seconds; the host drops players silent for 10 seconds.`，每 4 秒發一次 `PlayerPing` 心跳。也就是說：

- 加入方一旦停止心跳（Windows 用戶端因爲沒進世界而退出房間、或網路斷了），**10 秒後房主就把它從玩家列表裏刪掉**，看起來像「玩家沒顯示 / 房間停了」。
- 房主本身不會因此關房。`--skip-probe` 已經短接了 `MonitorHostAsync` 裏的 `MinecraftProbe.IsServerAsync`，所以 Linux 房主不會觸發 `Link.Error.WorldClosed`。
- **但 Windows 官方用戶端作爲房主時沒有這個短接**：`MonitorHostAsync` 每 6 秒探一次世界連接埠，連續 3 次失敗就 `Link.Error.WorldClosed` 關房。這才是「建房後沒多久自己停了」的更常見原因，`windows-test/` 的假世界樁正好讓它一直探得到。
- 若 Linux 端進程真的退出，那一定是 `LinkStage.Failed`（`CliHost.Drive` 會打印「連線失敗：…」並退出）。v2 腳本會把這行、退出碼和日誌尾部一起打印出來，不用再靠截圖猜。

## 後續階段（按順序）

1. ~~**階段 1 驗收（唯一硬標準）**~~ → **已於 2026-11-18 完成**，見開頭「階段 1 驗收結果」。復現：
   ```bash
   curl 192.168.1.5:8000/step4.sh > step4.sh   # IP 換成服務視窗打印的
   bash step4.sh          # 選 1 = 方向 A；選 3 = 方向 B
   ```
   方向 B 之前先在 Windows 雙擊 `windows-test/start-fake-world.bat` 並**保持視窗開着**（關掉後房主會 `WorldClosed` 自動關房）。Windows 端問 `campfire:mods` / `voice` 收到「模組不可用」是**預期降級**，不算失敗。「公開到連線大廳」按鈕不要點（原始碼建置 `IsAvailable=false`，且按約定不連作者官方服務）。
2. **階段 2（分兩批）**：Avalonia 介面重建。原桌面 UI 是 WPF/XAML（28 個 XAML / 約 1.9 萬行），Linux 無法編譯；階段 1 已把 `Link/` 協定層完整復用進 `net8.0` CLI。

   **第一批（已交付）**：`linux/Campfire.Avalonia/` —— 共享原始碼鏈接復用協定層（不引用 `campfire.dll`）、`SessionService` 長駐單例直連 `LinkSession.Changed`、靜態篝火橙配色（照抄 `Colors.xaml`）、680×598 自繪標題欄 + 10 層陰影 + 4 個膠囊 Tab、設定頁 132px 側欄 6 分區、自建大廳 list + join。地圖/工具 Tab 按約定保留結構 + 說明條，個性化分區先放說明條。

   **第二批（已交付，2026-11-19）**：完整動態主題（OKLCH 算法 + 9 種子色 / 6 風格 / 深淺兩套 / 400ms 過渡、gsettings 深淺、GNOME 強調色、SkiaSharp 桌布取色）、設定頁個性化分區重建、地圖 Tab 本機世界只讀掃描、工具 Tab 元件自檢 / 大廳啓停 / 日誌暫停。詳見下面「動態主題」章節。

   ### 第一批怎麼跑到虛擬機器裏（一行，免剪貼簿）

   Windows 側保持 `python linux/serve-for-vm.py` 視窗開着，Bazzite 終端敲：

   ```bash
   curl 192.168.1.5:8000/step5.sh > step5.sh   # IP 換成服務視窗打印的
   bash step5.sh
   ```

   **兩條都不要帶 `<` `>` 尖括號，也不要帶 `-fsS`**（見本節開頭的警告）。一行版也可以：`curl 192.168.1.5:8000/step5.sh | bash`。

   `step5.sh` 會更新原始碼包、下載並校驗離線 NuGet 包（約 167 MB）、生成執行時 `NuGet.Config`、離線 restore、全量編譯、`fc-list` 檢查中文字體，最後前臺啓動 GUI（Ctrl+C 結束）。腳本內部會自動探測主機服務地址，候選含預設網關與 `192.168.1.5` / `172.16.202.127`；若主機 IP 都不在列表裏，用 `CF_HOST_IP=實際IP bash step5.sh` 顯式指定。以後啓動 GUI 用短命令 `bash ~/campfire-gui.sh`。

   > 爲什麼需要離線包：Avalonia 11 是本分支**首次引入 NuGet 依賴**，虛擬機器直連公網慢/斷。25 個 `.nupkg` 全閉包放在 `linux/offline-nuget/`，壓成 `offline-nuget.tar.gz` 單獨走 HTTP，不進原始碼 tarball（`pack-for-linux.sh` 已排除）。`NuGet.Config` 帶 `<clear/>`，只在 VM 執行時生成、不入庫，否則會擋住 Windows 側聯網還原。
   > GUI 走 XWayland，需要有效 `DISPLAY` 與合成器；透明視窗異常時加 `XLIB_SKIP_ARGB_VISUALS=1`。Linux 沒有微軟雅黑，字體按 Noto CJK → 思源黑體 → 文泉驛 → Inter 回退。

   ### 介面還原補充：全屏適配 + 動態營火圖標（2026-11-19）

   用戶反饋「全屏會畸形」，並要求把標題欄靜態火焰換成動態圖標（用戶提供 `Campfire_JE2_BE2.gif`）。兩處都已完成：

   - **動態營火**：該 GIF 是 300×300 / **40 幀 / 每幀 100 ms / 帶 NETSCAPE 循環標記**。用 Python 標準庫（不引入任何第三方依賴）解碼，逐幀縮放到 96×96 後橫向拼成 `Assets/CampfireFrames.png`（3840×96，13 KB，四角全透明），以 `EmbeddedResource` 嵌進 dll。`Controls/CampfireLogo.cs` 用 `DispatcherTimer` 按 100 ms（10 fps，與原版 `Controls/CampfireLogo.cs` 一致）裁剪幀條繪製，視窗不可見時自動停表。原版那兩層 Path 火焰已刪除。素材版權見 `THIRD-PARTY-NOTICES.md`。
   - **全屏布局**：主頁 3 張大磁貼改成 `Grid ColumnDefinitions="*,10,*,10,*"` 等寬鋪滿 + `MinHeight="124"`（視窗拉高時不變扁），下排 2 張各佔一半；限寬居中從 `HomeView` 上提到 `MainWindow` 的 `ContentControl`（`MaxWidth="1400"`），**所有 Tab 統一生效**，避免地圖/工具/設定頁在超寬屏上文字擠在左邊。另去掉了未生效的垂直居中代碼。
   - **視窗按鈕對齊**：原版 `MainWindow.xaml:151/166` 的減號/叉號 Path **不設 Width/Height**，WPF 按幾何邊界自動居中；初版移植給每個 Path 都套了 `Width="10" Height="10" Stretch="None"`，導致 `M0,0 L10,0` 那條減號被固定在 10×10 框**頂部 y=0**，比居中的方框 `Rectangle`、叉號高了約 5px（用戶反饋「不齊」）。現改爲統一 12×12 框、幾何中心都在 (6,6)：減號 `M1,6 L11,6`、叉號 `M1,1 L11,11 M11,1 L1,11`，方框保留原版 10×10 微圓角 `Rectangle`；並還原原版的 `StrokeLineCap="Round"` 與第 2/3 個按鈕 `Margin="4,0,0,0"`。2880×1704 截圖裁切放大實測三者已對齊。
   - Windows 開發機以 **2880×1704 最大化實測**：主頁選中、磁貼等寬無錯位、營火像素動畫顯示正常、中文無方塊。內容下方留白是全屏下內容不足的正常現象，不是畸形（原版視窗固定 680×598 且不可縮放）。

   **有意不還原**：下排首卡是「連線診斷」而非「最近房間」（沒有房間歷史）。右上「繁」圓按鈕已於 2026-11-26 還原並升級爲 **簡 / 繁 / EN 三語切換**，見「介面本地化」一節。

   ### 第一批驗收：GUI 雙向互通回歸（2026-11-19 已通過 ✅）

   階段 1 的雙向互通原本用 CLI（`step4.sh`）驗；GUI 的 `SessionService` 於 2026-11-19 按下面清單在 VM 裏完成方向 A / B 雙向回歸，兩端均爲「玩家 2 / 直連」。以下保留爲可復現的驗收清單。

   先確認沒有殘留會話（兩個 EasyTier 實例會搶同一個虛擬網路名）：

   ```bash
   pgrep -a easytier-core
   ```

   有輸出就先停掉（`bash step4.sh` 的視窗按 Ctrl+C）。然後啓動（自檢模式，預設不勾「自動開放區域網路世界」、連接埠 43210）：

   ```bash
   bash ~/campfire-gui.sh
   ```

   **方向 A：Linux GUI 建房 → 官方 Windows 用戶端加入**

   1. GUI 主頁點「建立房間」磁貼 → 建房頁，連接埠保持 `43210`，**不要勾**自動開放 → 點「開始」。
   2. 「邀請碼」卡片出現大字 `U/XXXX-XXXX-XXXX-XXXX`。
   3. Windows 雙擊 `C:\Users\yzhhh\Downloads\Campfire.exe` →「加入房間」→ **手敲**那個碼（剪貼簿不可用）。
   4. 判據：GUI「房間狀態」顯示 `玩家：2`，Windows 端玩家列表 2 人 / 直連。

   **方向 B：官方 Windows 用戶端建房 → Linux GUI 加入**

   1. GUI 點「停止」。
   2. Windows 雙擊 `windows-test\start-fake-world.bat` 並**保持視窗開着**，官方用戶端診斷頁「重新檢測」→「建立房間」，記下邀請碼。
   3. GUI 主頁點「加入房間」→ 輸入邀請碼 → 點「開始」。
   4. 判據：GUI「房間狀態」顯示 `玩家：2` / 階段 `Ready`，Windows 端玩家 2。

   **兩個方向都通 = 第一批完成**（2026-11-19 用戶實測：方向 A / B 均 `玩家 2 / 直連`，已閉環）。自檢模式只驗房間層（邀請碼 → EasyTier 組網 → 房間協定 → 玩家列表），進 MC 世界仍不行（VM 沒裝 MC），屬預期降級。

   **回歸時發現並修復的 3 個 UI bug**：

   | 現象 | 根因 | 修復 |
   |---|---|---|
   | Windows 端退出 / 點「停止」後仍顯示「玩家 2」和上一次的邀請碼 | `LinkSession.Stop()`（:1332-1342）只重置 `Role/Stage/Error`，**不清** `Players/Code/DirectPeers/ConnectedNodes/LocalPort`；`SessionService.Refresh()` 又無條件照抄 | `Refresh()` 在 `!IsActive` 時調 `ClearVisualState()` 歸零；`Host/Join/Stop` 開頭也先清；**協定層副本一行未改** |
   | 「加入房間」頁冒出一張「邀請碼」卡片，還寫着「把這個碼手敲進 Windows 官方用戶端」 | `StartJoin`（:475）`Code = code` 把**輸入的邀請碼**存進 `Code`，而該卡片沒有 `IsVisible` 條件 | 卡片加 `IsVisible="{Binding Hosting}"`，文案改「發給朋友」 |
   | 兩頁除了標題漢字幾乎沒區別 | 兩頁共用 `LinkView`，下面三張卡片完全相同 | 加入頁新增「連接資訊」卡片（`127.0.0.1:<LocalPort>`，未就緒顯示「等待房間就緒…」）、按鈕改「加入房間 / 退出房間」；建房頁保留邀請碼卡片；「本機連接連接埠」行只在 `HasConnection` 時顯示 |

   驗證：反射探針模擬殘留字段後 `Refresh()` 全歸零（PASS）；實際 GUI 截圖確認加入頁無邀請碼卡片、有連接資訊卡片、狀態「未開始 / 已停止」、玩家 0。

   > 並行線（不阻塞第一批）：VM 裏裝真實 MC + 完整 JDK 才能實測「自動開放區域網路世界」端到端。`step6.sh` 已裝好 JDK、`GameMappings` 已移植，代碼側三塊前置齊備，只差遊戲本體。

## 動態主題（階段 2 第二批，2026-11-19 已交付）

### 檔案與逐字移植範圍

| 檔案 | 來源 | 改動 |
|---|---|---|
| `Campfire.Avalonia/Themes/ColorMath.cs` | `src/Campfire/Themes/ColorMath.cs` | **逐字移植**：Oklch / sRGB↔Oklab 矩陣 / gamma / 色域二分 18 次 / Parse / Contrast。僅把 `System.Windows.Media.Color` 換成 `Avalonia.Media.Color`（`FromRgb` / `FromArgb` / `R,G,B` 籤名相同） |
| `Campfire.Avalonia/Themes/ThemeManager.cs` | `src/Campfire/Themes/ThemeManager.cs` | `ThemeMode` / `PaletteStyle` / 9 個預設 / `Palette.Create` / `FillLight` / `FillDark` / `SetRamp` / `SetGrays` **逐字移植**；設定鍵名 `theme.mode/monet/monet.index/color/style` 不變，直接復用 Linux 版 `AppSettings.Get/Set` |
| `Campfire.Avalonia/Themes/LinuxThemeSources.cs` | `src/Campfire/Themes/WallpaperColors.cs` | `Score()`（360 色相分箱、覆蓋率/彩度評分、90→15 度間距挑 4 個候選）與 `SampleSize=128` / `MaxColors=4` **逐字移植**；Win32 / 登錄檔調用全部替換 |
| `App.axaml.cs` | — | 啓動時 `ThemeManager.Initialize()`，退出時 `Shutdown()` + `Session.Shutdown()` |
| `Views/PreferencesView.axaml` + `ViewModels/Pages.cs` | `Pages/PreferencesPage.xaml(.cs)` | 132px 側欄 6 分區殼不動；個性化分區重建爲 5 張卡片（模式 / 風格 / 9 色板 + 自定義 hex / 桌布取色 / 當前狀態） |

### Linux 替代實現（與 Windows 原版的差異，如實清單）

| 原版（WPF） | Linux 版 | 差異說明 |
|---|---|---|
| `Registry ... AppsUseLightTheme` | `gsettings get org.gnome.desktop.interface color-scheme` → `gtk-theme` → `GTK_THEME` → 淺色 | 逐級降級；沒有 GNOME 時一律按淺色 |
| `SystemEvents.UserPreferenceChanged` + 600ms 去抖 | `System.Threading.Timer` **3 秒輪詢**，值變化才 `Apply(true)` | 系統深淺切換最多延遲 3 秒，不是即時事件；輪詢在線程池跑，不卡 UI |
| `DwmGetColorizationColor`（任意強調色） | `gsettings ... accent-color` 映射到 **GNOME 9 色枚舉**（blue/teal/green/yellow/orange/red/pink/purple/slate） | 只能取 9 個預設色，取不到回退篝火橙 `#E2680C` |
| `SystemParametersInfo(GetDeskWallpaper)` + GDI+ 縮放 | `gsettings ... picture-uri(-dark)` → `file://` 轉路徑 → **SkiaSharp 2.88.9** 解碼縮放 128×128 | 只覆蓋 GNOME 靜態桌布；幻燈片 / KDE / Wayland 桌布協定未覆蓋，另有 `~/Pictures/wallpaper` 等兜底路徑 |
| `Registry Control Panel\Colors\Background`（純色桌面） | `gsettings ... primary-color` | 格式不同，語義等價 |
| `SolidColorBrush.BeginAnimation(ColorAnimation, 400ms CubicEase)` | `DispatcherTimer` **20ms 逐幀插值**，每幀替換 `Application.Resources` 頂層條目 | 視覺效果等價，但不是 WPF 的渲染層動畫；切換時會有約 40 個鍵 × 20 幀的字典寫入 |
| `Window.Resources` 覆蓋 | `Application.Current.Resources` 頂層覆蓋（先於 `MergedDictionaries` 命中） | — |

**未 100% 還原的部分**：DWM 任意色、登錄檔、系統主題即時事件、GDI+ 縮放、`Color1..8` 作爲 `StaticResource` 被引用時不會隨動態值變化（UI 實際用的是 `Brush1..8`，已覆蓋）。`BrushWhite` 保持白色（標題欄文字在兩套主題下都可讀），與原版一致。

### 地圖 / 工具 Tab 真功能

- **地圖**：`WorldScanner` 只讀枚舉 `~/.minecraft/saves`、PrismLauncher / MultiMC / Flatpak 實例的 `saves`，列出世界名與 `level.dat` 修改時間；「匯入地圖」頁列出全部檢測路徑。**刻意不寫存檔**（不做解壓匯入），避免誤改用戶的世界。
- **工具**：`EasyTierPackage.CanRunAsync()` 元件自檢按鈕、`LobbyService` 的 HTTP 狀態探測 + 啓動/停止（**只停止本 GUI 自己啓動的進程**，絕不 kill 別處起的服務）、日誌「自動重新整理」複選框真正暫停 `SessionService` 的 1 秒輪詢。

### 驗證

`bash build.sh` 三工程 0 警告 0 錯誤 + selftest 11 項全過；Windows 開發機實啓動 GUI 截圖核對：預設淺色橙、`theme.mode=dark` + 藍色種子 + `Vibrant` 深色介面、設定頁點第 5 個色板實時變藍（`Apply(true)` + 400ms 過渡）、地圖頁 / 工具頁新控件全部渲染正常。

## 自動開放區域網路世界（Java Agent，2026-11-19 恢復）

**爲什麼階段 1 要手動開**：當時把 `JavaAgent` / `LanOpener` / `Worlds.RunningGame` 當成「Windows 專屬」一起裁掉，並把 `LinkSettings.AutoOpenLan` 強制設成 `false`。複查原始碼後確認這是**過度裁剪**：

- 原版 `AutoOpenLan` **預設 `true`**（`LinkSettings.cs:81-85`）。
- Java 側 `src/java-agent/` 的 3 個 jar 與 5 個 `.java` 是**純 JVM**：`com.sun.tools.attach.VirtualMachine.attach(pid)` 注入後，反射調用遊戲自己的 `publishServer(...)` —— 也就是「對區域網路開放」按鈕背後的那個方法。整套在 Linux 上同樣成立。
- `LanOpener.cs` / `AgentState` 只是往資料目錄寫 `lan-<pid>.txt` / `overlay-<pid>.txt` 並每 3 秒重新整理，**零平臺依賴**。
- `LinkSession.cs` 的 Linux 副本裏 `OpenWorldToLanAsync` **本來就完整保留**（階段 1 只加了 `SkipProbe`），所以協定層一行都沒改。

### 本分支補的 5 處 Linux 適配

| 原 Windows 實現 | Linux 版 |
|---|---|
| `JavaAgent.FindAttachJava` 找 `java.exe` / `Program Files` / 登錄檔 | `Linux/GameAgent.cs`：找 `CAMPFIRE_ATTACH_JAVA`、`JAVA_HOME/bin/java`、`/usr/lib/jvm/*/bin/java`、啓動器 runtime、PATH，逐個 `--list-modules` 檢查 `jdk.attach@` |
| `Worlds.RunningGame.Find()` 用 WMI 枚舉進程 | `Shims/Worlds.cs`：讀 `/proc/<pid>/exe` + `/proc/<pid>/cmdline`，認 MC 主類或 `--gameDir` |
| `LocalWorldFinder` 用 `iphlpapi.dll` 找連接埠 | `Linux/LocalWorldFinder.cs`：`/proc/net/tcp(6)` 的 LISTEN inode → `/proc/*/fd` → 只認 MC 進程持有的 socket |
| 3 個 jar 是 `EmbeddedResource` | 兩個 csproj 各自嵌入；`pack-for-linux.sh` 把 `src/java-agent/` 原始碼與 jar 一起收進部署包（GPL-3.0 完整對應原始碼） |
| `GameMappings.FindVersionFile` 用 `;` 與 `\libraries\` | `Linux/GameMappings.cs`：classpath 按 `:` 拆、認 `/libraries/`，沿版本 json 的 `inheritsFrom` 找 `downloads.client_mappings` 的 URL/SHA-1；BMCLAPI 鏡像優先、單源超時 30s、按內容哈希緩存。`GameAgent.LoadAsync` 把結果拼成 `state=...\|mappings=...` 傳給 Agent |

### 怎麼用

**① 裝完整 JDK（一次即可，約 185 MB）**

Windows 側先跑 `bash linux/fetch-jdk.sh`，把 Temurin 17.0.21+9 的 Linux x64 tarball 下到 HTTP 目錄（`linux/`），並保持 `serve-for-vm.py` 視窗開着。虛擬機器裏手敲一行：

```bash
curl 192.168.1.5:8000/step6.sh | bash
```

IP 以服務視窗打印的為準，別套 `<` `>`。`step6.sh` 會下載 + `sha256sum -c` 校驗 + 解壓到 `~/.local/campfire-jdk`（`--strip-components=1`），驗證 `bin/java` 能跑且 `--list-modules` 裏有 `jdk.attach@`，然後寫 `~/campfire-jdk-env.sh` 並重寫 `~/campfire-gui-autolan.sh` 讓它自動加載 `CAMPFIRE_ATTACH_JAVA` / `JAVA_HOME`。已裝好並校驗通過就跳過下載。`step5.sh` 生成的入口也會 source 這個片段，所以兩個腳本誰先跑都行。

> **版本提醒**：這個 JDK 17 只適用於 1.18–1.20.4（以及作爲舊版 MC 的 attach 端）。**Minecraft 26.1 起要求 Java 25**，裝 26.3 請直接走下下節的 `step7.sh`，它會裝 Temurin 25 並生成 `campfire-gui-autolan-26.sh`，不覆蓋這裡的舊入口。

**② 啓動正式連線入口**

```bash
bash ~/campfire-gui-autolan.sh
```

這個入口不跳過世界探測，並預設勾上「自動開放區域網路世界」。建房頁勾上後進入一個單人世界，Campfire 會自動開放並發現連接埠，**不用手點、不用填連接埠**。老的 `bash ~/campfire-gui.sh` 是房間層自檢模式，勾選會被明確攔下，不會卡死。

也可手動設 `CAMPFIRE_ATTACH_JAVA=/path/to/jdk/bin/java`，日誌會打印選中的 java。工具 Tab 有「自動開放區域網路（Java Agent）」卡片，顯示開關、jar 與環境變量狀態。

### 硬前提與已知邊界（誠實清單）

- **必須有真實 Minecraft**：`windows-test/` 的假世界樁只能騙過「已開放區域網路」的探測，沒有遊戲內 `publishServer` 可調。2026-11-21 已按用戶指定的 **26.3** 備好離線包（`mc-26.3-offline.tar.gz` + JDK 25 + `step7.sh`），裝完即可在 VM 裏跑真遊戲。
- **必須有帶 `jdk.attach` 模塊的完整 JDK**：MC 啓動器自帶的通常是 JRE，沒有這個模塊；Bazzite 不可變系統也不能 `dnf install`。已備好 `fetch-jdk.sh` + `step6.sh`：Windows 側從清華 Adoptium 鏡像下 Temurin 17.0.21+9（與本機 Windows JDK 同大版本），走已驗證的 HTTP 通道送進 VM，解壓到 `~/.local/campfire-jdk` 並自動設好 `CAMPFIRE_ATTACH_JAVA`。不需要 sudo、不改系統層。
- **同一用戶、目標 JVM 未禁 attach**：Linux attach 走 `/tmp/.java_pid<pid>`，沙箱或 `-XX:+DisableAttachMechanism` 會擋住。
- **MC 版本與混淆**：Hook 支援 1.14–26.x，按官方名反射查找方法。按 `CampfireNames.java:22-24` / `GameMappings.cs:15`，**1.14.4–1.21.11 的官方啓動器正式版都是混淆版**（Mojang 另發 `client_mappings` 供還原），**26.1 起才不混淆**；Fabric/Forge 等以 Mojmap 官方名執行的加載器也不需要映射。本分支已移植 `Linux/GameMappings.cs`：從 `/proc/<pid>/cmdline` 拿 classpath、沿版本 json 的 `inheritsFrom` 找 `downloads.client_mappings` 的 URL/SHA-1，優先走 BMCLAPI 國內鏡像（單源 30s），按 SHA-1 緩存到 `DataDirectory/mappings/`；緩存裏已有同名檔案就直接復用，**可在 Windows 側預下好後放進 VM 的資料目錄，完全不用聯網**。1.7.2–1.14.3 的老版本名字表隨程式內嵌（`Mappings/*.txt`）。若所有下載源都失敗，日誌會警告並退回「只按官方名查找」，功能降級但不卡死。
- 失敗不會卡死：`OpenWorldToLanAsync` 會打日誌並把 `AutoLan` 標成 `Unsupported`；取消勾選、手動填連接埠仍可正常連線。
- **Linux 端到端已真機確認**（2026-11-25）：26.3 的元資料、Hook 反射目標、ASM 對 Java 25 字節碼的支援先由 Windows 側靜態驗證，隨後在 Bazzite 用 `step7.sh` + 單人世界 + `campfire-gui-autolan-26.sh` 實測通過，Windows 官方用戶端 `0.8 ms` 直連、兩端 2 人。詳見「Minecraft 26.3 端到端實測」一節。

## Minecraft 26.3 端到端實測（2026-11-25 已通過 ✅）

> **2026-11-25 16:58 真機結果**：MC 26.3 視窗標題 `Minecraft 26.3 - Multiplayer (LAN)`、聊天欄「Campfire 已自動對區域網路開放，連接埠 …」；GUI 建房頁 `階段 Ready · 玩家 2 · 直連 1 · 中轉 0 · 節點 4`；Windows 官方用戶端 `127.0.0.1:42485`、`0.8 ms`、`直連 · TCP`、玩家 2。**自動開放區域網路端到端通過，項目全部閉環。** 離線賬號下 26.3 的 `prepareKeyPair()` 也正常，此前標爲最大未知的風險已排除。

用戶指定 **Java 版 26.3**（正式版，2026-09-15 發布，Wilderness Bound / 奔赴荒野）。2026 年起 Mojang 按年份編號，26.1 / 26.2 / 26.3 是當年三個 game drop；**1.21.11 是最後一個舊編號、也是最後一個混淆版**。

### 靜態預檢結論（Windows 側，未啓動遊戲）

| 檢查項 | 結果 |
|---|---|
| 版本元資料 | `26.3.json` → `javaVersion.majorVersion=25`；`downloads` 只有 client / server，**沒有 `client_mappings`** |
| 混淆 | 26.1 起 Mojang 取消混淆 → **26.3 不需要任何映射表**，40 個 `Mappings/*.txt` 與本版本無關 |
| Hook 目標 | `javap` 打開 26.3 client.jar：`IntegratedServer.publishServer(MultiplayerScope,int)` 仍在（字節碼調 `startTcpServerListener` 並寫 `publishedPort`）；`tickServer(BooleanSupplier)`、`storageSource`、`HttpUtil.getAvailablePort()`、`ClientboundTabListPacket(Component,Component)` 都在 |
| ASM | 9.11.1 的 `ClassReader` 實測能讀 **class file major 69**（Java 25），Transformer 不會因新字節碼崩潰 |
| 結論 | **Java Agent 與 C# 側一行都不用改**，缺的只是真實遊戲 + Java 25 |

> 註：`publishServer` 在基類 `MinecraftServer` 裏是三參 `(MultiplayerScope,boolean,int)`，但 Hook 在執行時類 `IntegratedServer` 上查找，那裡保留了兩參重載，所以「26.2+ 分支」能命中。26.3 的該方法在開放前會調用 `ClientPacketListener.prepareKeyPair()`；**2026-11-25 真機實測離線賬號下正常通過**。

### 交付物

| 檔案 | 大小 | 說明 |
|---|---|---|
| `linux/fetch-mc263.py` | — | Windows 側預下器，可復現、斷點續傳、逐檔案 SHA-1 校驗 |
| `linux/mc-26.3-offline.tar.gz` | 497 MB | 版本 json + client.jar 40 MB + 66 個 Linux 庫 80 MB + 12 個 natives `.so` + 資產索引 34 + 5149 個資源對象 465 MB |
| `OpenJDK25U-jdk_x64_linux_hotspot_25.0.5_8.tar.gz` | 135 MB | Temurin JDK 25（清華 Adoptium 鏡像），遊戲與 attach 共用 |
| `linux/step7.sh` | — | VM 側一鍵安裝 + 生成離線啓動與連線入口 |

`fetch-mc263.py` 按版本 json 的 `rules` 只挑 Linux 庫（114 → 66），把 `natives-linux` jar 裏的 `.so` 解到 `natives/java`，並生成 `classpath.txt`。26.3 用 LWJGL 3.4.3 + **SDL3**（不再是 GLFW），natives 是獨立 artifact，必須一併帶上。

### 爲什麼必須換 JDK 25

`step6.sh` 的 Temurin 17 只覆蓋 1.18–1.20.4；26.1 起遊戲 class file 是 69，Java 17/21 都拒絕加載。`step7.sh` 把**遊戲 JVM 與 attach JVM 統一到同一個 JDK 25**，既滿足遊戲要求，也避開「JDK 17 attach 到 Java 25 進程」的跨大版本風險。舊的 `campfire-gui-autolan.sh`（JDK 17）保留給舊版 MC，互不影響。

### VM 裏怎麼跑

```bash
curl 192.168.1.5:8000/step7.sh | bash
```

IP 以服務視窗打印的為準。之後開兩個終端：

- **終端 A**：`bash ~/minecraft-26.3.sh` — 不開啓動器、不要正版賬號，直接按版本 json 拼 classpath 離線啓動（`step7.sh` 預設已生成 X11 + llvmpipe 低配版）；`--gameDir ~/.minecraft`，所以地圖 Tab 能掃到新建的世界。進主菜單 → 單人遊戲 → 新建世界 → **保持世界開着**。
- **終端 B**：`bash ~/campfire-gui-autolan-26.sh` — `CAMPFIRE_AUTO_LAN=1` + 不跳過世界探測 + `CAMPFIRE_ATTACH_JAVA` 指向 JDK 25。
- GUI 主頁「建立房間」→「自動開放」已預設勾上 →「開始」。

**判據**：GUI 工具頁日誌出現 `Java agent loaded into game process` 與 `Opened <世界名> to LAN on port N`，房間狀態不再停在「等待世界連接埠」，Windows 官方用戶端能加入並看到 2 人。

排錯：認證報錯試 `MC_USER_TYPE=legacy bash ~/minecraft-26.3.sh`；SDL3 起不來試 `MC_SDL_DRIVER=x11`（或加 `LIBGL_ALWAYS_SOFTWARE=1`）；GUI 找不到遊戲先 `pgrep -a java`。

### 已知邊界

- **端到端已於 2026-11-25 在 Bazzite 真機跑通**；上面的靜態預檢、離線包與低配參數都已由實測確認。
- 26.3 在 LAN 開放前調 `prepareKeyPair()`，**實測離線賬號也能通過**；無需正版登入，也無需退回 1.21.11。
- VMware SVGA 軟體渲染下 26.3 只能低配跑（X11 + llvmpipe + 800×600 + 低畫質），只驗功能、不評性能；`step7.sh` 預設入口已按此固化。
- Minecraft 用戶端與資源版權歸 Mojang / Microsoft；離線包由使用者本機下載、只在自己的 VM 內使用，**不隨 `campfire-linux.tar.gz` 分發**。

### 真機踩坑：classpath（2026-11-21 已修）

第一次在 Bazzite 上跑終端 A 報：

```
錯誤: 找不到或無法加載主類 net.minecraft.client.main.Main
原因: java.lang.ClassNotFoundException: net.minecraft.client.main.Main
```

這是 `ClassNotFoundException` 而**不是** `UnsupportedClassVersionError`，所以與 Java 25 無關。根因是離線包的 `classpath.txt` 在 Windows 側生成時踩了兩個坑：

1. 用 `pathlib.Path` 拼路徑 → 寫出 352 個反斜槓 `\`，Linux 的 `-cp` 不認，66 個庫和 client.jar 全都沒進有效 classpath；
2. 用 `write_text` 寫行尾 → CRLF，最後一個 jar 路徑尾巴帶 `\r`。

`fetch-mc263.py` 已改成**正斜槓字符串拼接 + 二進制寫 LF**（`classpath.txt` 與說明文本都改）。重打的包 sha256 `18ffd826…0c0`，實測 67 個條目全部與包內路徑精確匹配、0 反斜槓、0 CR。`step7.sh` 解壓後也會自動自檢並重建。

**已經裝好的 VM 不用重下 497 MB 包**，跑一個約 4 KB 的修復腳本就地重建 `classpath.txt` 即可：

```bash
curl 192.168.1.5:8000/step7-fix.sh | bash
```

然後重新跑終端 A。這和之前 `.sha256` 的 CRLF 是同一類 Windows 文本/路徑坑，當時只修了校驗和檔案，漏了 classpath。

### 真機踩坑：26.3 主菜單 UI 不渲染（2026-11-25 已解決）

VM 裏遊戲視窗能彈出來，但只剩一張模糊的 **Dappled Forest 主菜單全景**（26.3 換的新背景），LOGO、按鈕、文字、HUD 一個都沒有，畫面一直不動 —— 這是**渲染層卡住**，不是"還在加載"。

查證到的 26.3 背景：

- 26.3 把視窗/輸入庫從 GLFW 換成 **SDL3**；官方快照就列過 Wayland 獨佔全屏必崩，Linux/Wayland 是高風險組合。
- 26.2 起支援 `--graphicsBackend`（`default` / `opengl` / `vulkan`）；**26.3 正式版預設仍是 OpenGL**，26.4 快照起才把預設改成 Vulkan。軟體渲染設備可顯式切到 Vulkan 試。
- 26.3 有已知 hang（Mojira MC-311807）：SDL3 的同步調用阻塞渲染線程，`latest.log` 會**突然停止、無異常、無崩潰報告**，與"卡住"表現吻合。
- VMware SVGA + llvmpipe 下另有一類慢/卡：官方建議 `LIBGL_ALWAYS_SOFTWARE=1`、渲染距離 2–4、視窗化、關 shader；也可在 `.vmx` 裏開 3D 加速（`accel3d.enable` / `mip.avail`）。

`linux/step7-gfx.sh` 做的三件事：

1. **診斷**（把結果截圖回傳即可定位）：`pgrep -a java`、`latest.log` 尾部 25 行、`saves/` 世界列表、`options.txt`、`glxinfo`、Vulkan ICD。
2. **降負載**：備份並重寫低畫質 `options.txt`（渲染/模擬距離 2、30 FPS、關雲/粒子/實體陰影/VSync、視窗化）。
3. **兩個低配啓動腳本**：`minecraft-26.3-low.sh`（XWayland + 強制 llvmpipe + 800×600 + `-Xmx1G`，沿用預設 OpenGL）與 `minecraft-26.3-vk.sh`（同配置但 `--graphicsBackend vulkan`）。兩者都支援 `MC_QUICK_WORLD=<saves 裏的檔案夾名>` → `--quickPlaySingleplayer`，**主菜單點不動也能直接進世界**繼續測自動開放區域網路。

```bash
curl 192.168.1.5:8000/step7-gfx.sh | bash
```

先 Ctrl+C 結束卡住的遊戲，再跑 `bash $HOME/minecraft-26.3-low.sh`；不行換 `-vk`。**2026-11-25 真機已確認：`-low` 能讓主菜單完整渲染**（LOGO、Singleplayer 等按鈕全可見），**不必切 Vulkan**；根因是 Wayland + SDL3 預設路徑疊加 VMware 軟體渲染負載。若仍救不了，務實退路是開 VMware 3D 加速，或退回 **1.21.11**（GLFW + JDK 21 路線仍在）先驗證 Agent 邏輯，26.3 保留靜態相容結論。

主菜單出來後：點 **Singleplayer → Create New World**（世界名建議用簡單英文）→ 進入世界並保持視窗 → 另一個終端跑 `bash $HOME/campfire-gui-autolan-26.sh` → GUI 建立房間（「自動開放」預設勾上）→ 開始。判據是工具頁日誌出現 `Java agent loaded into game process` 與 `Opened ... to LAN on port N`。若離線賬號卡在 26.3 的 `prepareKeyPair()` 導致 `Open to LAN failed`，降級路徑是手動點「對區域網路開放」拿連接埠、取消勾選後手填連接埠連線。

## 介面動畫（2026-11-25，完整版）

最初只有「營火 10fps 幀動畫」與「主題 400ms 色彩插值」，頁面/卡片/按鈕都是瞬間出現。第一版補了三類輕量動效，本版把它擴成**統一的多通道時間軸**，在不增加重繪負擔的前提下把該有的反饋都補齊。

### 動效清單

| # | 動效 | 觸發 | 參數 |
|---|---|---|---|
| 1 | 視窗開場 | 啓動 | PanRoot 淡入 + 0.972→1 縮放 + 8px 上浮，420ms QuintOut（陰影層一起動） |
| 2 | 頁面入場 | 切 Tab / 進子頁 / 返回 | 卡片錯峯 46ms、單張 300ms；淡入（前 62% 完成）+ 18px 位移帶 6% 回彈 + 0.965→1 縮放，QuintOut / BackOut |
| 3 | 入場方向感 | 同上 | Tab 右移 +16px / 左移 −16px、進子頁 +30px、返回 −30px；由 VM 換頁前告知 `NavDirection` |
| 4 | 卡片內容跟進 | 同 #2 | 卡片內前 5 個子元素再錯峯 26ms、延遲 96ms、220ms 淡入 + 6px 上浮 |
| 5 | 狀態點呼吸 | 常駐 | 空閒 2.8s 慢呼吸 / 連接中 0.95s 快脈衝 / `Ready` 常亮 |
| 6 | Ready 光環 | 會話進入 `Ready` | 標題欄圓環 0.6→2.8 倍擴散 + 淡出，620ms |
| 7 | 按鈕三態 | 懸停 / 按下 / 鬆開 | 懸停 1.035（磁貼 1.018、色板 1.15、標題欄 1.10）+ 磁貼上浮 2.5px，190ms；按下 0.945，90ms；鬆開 BackOut 300ms 彈性回彈 |
| 8 | 點擊漣漪 | 任意按鈕按下 | 頂層 `FxLayer` Canvas 裏按點擊點生成圓形，0.08→1.26 擴散 + 淡出，480ms；取當前 `Brush3` 作色，不參與布局、不攔截命中 |
| 9 | 卡片輕抬升 | 指針進入 / 離開 | 1.004 縮放 + 上浮 1.5px，200ms（與入場通道共存） |
| 10 | 數值變化脈衝 | `pcl-live` 文本更新 | 1→1.10 鐘形縮放 + 2.5px 浮起，260ms；用於房間狀態、玩家數、連接地址 |
| 11 | 主題色板彈出 | 點擊色板 | 1→1.20 脈衝 340ms，與 400ms 主題插值同步 |
| 12 | 邀請碼打字機 | `Session.Code` 變化 | 逐字寫入（26ms/字，約 0.5s），末尾輕微脈衝；關閉動畫時仍顯示全文 |
| 13 | 營火微搖曳 | 常駐 | 幀動畫之上疊 ±0.7px、3.1s 正弦，只動 RenderTransform |

### 引擎結構

- **通道合成**：每個控件一組 `Channels`（`Opacity` × `ScaleTransform` × `TranslateTransform`），入場 / 懸停 / 脈衝 / 搖曳各寫各的字段，`Apply()` 統一相乘。多條動畫疊在同一控件上不會互相覆蓋——這是能同時開 13 種效果而不打架的前提。
- **通用時間軸**：`Track` 帶 delay / duration / easing（Linear、CubicOut、QuintOut、SineInOut、BackOut）/ complete 回調；`RunExclusive(owner, channel)` 保證同一控件同一通道只留最新一條（hover 與 press 不會互相打斷）。
- **共享 30fps `DispatcherTimer`**：所有瞬時動畫 + 持續項共用一個表；無 Track 且無持續項時自動停表。`CAMPFIRE_ANIM_FPS` 可設 15–60。
- **防重播**：`AttachedToVisualTree` 在換 Content 時可能觸發兩回，`Entered` 集合保證同一 View 只入場一次，否則動畫時長翻倍、看起來像卡片遲遲不出現。
- **`FinishAll()` 只收尾 `enter` 通道**：換頁時把上一頁卡片推到終態，但不會掐斷正在跑的視窗開場 / 光環 / 漣漪 / 打字機。
- **`Cleanup()`**：View 卸載時清理通道狀態、事件掛載與殘留 Track，避免持有已銷毀控件。

### 硬約束與性能護欄

- **只改 `Opacity` 與 `RenderTransform`，不碰任何隨主題變化的畫刷**。`ThemeManager` 每 20ms 替換 `Application.Resources` 頂層條目做 400ms 插值，給 `Background` 掛過渡會動畫套動畫、色彩拖影。漣漪只在建立時取一次 `Brush3` 快照。
- **不觸碰 `Width`/`Height`/`Margin`/`Padding`**，不觸發布局重算；軟體渲染（Bazzite + llvmpipe）下每幀只是若干次屬性賦值。
- 舊版 `Styles/Controls.axaml` 裏的樣式級 `TransformOperationsTransition` 已**移除**：`RenderTransform` 由引擎獨佔，避免兩套值互相覆蓋；XAML 只保留 `:pointerover` / `:pressed` 的色彩態。
- 同時 Track 上限 140，超限直接跳終態；漣漪同屏上限 15 個；`FxLayer` 全層 `IsHitTestVisible=False` + `ClipToBounds`。
- 與 `CampfireLogo`（10fps 幀動畫）、主題 400ms 插值互不幹擾。

### 開關

| 環境變量 | 效果 |
|---|---|
| `CAMPFIRE_ANIM=0` | 全部關閉：入場、呼吸、漣漪、脈衝、搖曳、營火幀動畫都靜止，介面功能完整可用 |
| `CAMPFIRE_ANIM=1` | 強制開啓（覆蓋系統「減少動畫」設定） |
| 未設定（Linux） | 尊重 GNOME `org.gnome.desktop.interface enable-animations`；無 gsettings 則預設開 |
| `CAMPFIRE_ANIM_FPS=15..60` | 調整共享 timer 幀率，預設 30 |

關閉時 `AttachTypewriter` 仍訂閱 `Session.Code` 並直接顯示全文，邀請碼不會丟失。

驗證：三工程 `0 警告 0 錯誤`、selftest 11 項全過；Windows 真啓動 GUI 抓取入場中間幀、穩定幀、漣漪幀、色板切換後的穩定幀，以及 `CAMPFIRE_ANIM=0` 模式截圖 —— 穩定幀中主頁與設定頁卡片全部完整清晰、無半透明殘留、無錯位、無光斑殘留。

## 介面本地化（2026-11-26 新增）

面向 GitHub 發布，介面與 `linux/` 文檔都提供 **簡體中文 / 繁體中文 / English** 三語。

| 層 | 做法 |
|---|---|
| 介面文案 | `Campfire.Avalonia/Localization/Loc.cs` + `Strings.cs`（188 鍵 × 3 語）。XAML 統一寫 `{Binding L[Key]}`，ViewModel 動態文案用 `Loc.Get` / `Loc.Format` |
| 語言按鈕 | 標題欄右上角還原原版「繁」圓按鈕的位置，改成 **簡 → 繁 → EN** 循環，滑鼠懸停顯示提示 |
| 持久化 | `settings.ini` 的 `ui.language`（`zh-Hans` / `zh-Hant` / `en`）；未設定時跟隨 `CultureInfo.CurrentUICulture`（zh-Hant → 繁體，非 zh → English） |
| 即時切換 | `LocSource` 索引器發 `Item[]` 通知重新整理靜態文案；`ViewModelBase` 通過弱引用訂閱 `RaiseAll()` 重新整理帶參數的動態文案；子頁標題存本地化鍵而不是譯文 |
| 文檔 | `README.md`（簡體）/ `README.zh-Hant.md`（繁體）/ `README.en.md`（English），頂部互相鏈接 |

設計對齊上遊 `src/Campfire/Localization/Text.cs`：純 C# 詞表 + `LanguageChanged` 事件 + 索引器綁定。上遊的 `TextExtension.cs` 依賴 `System.Windows.Data` / `Markup`（WPF），Avalonia 用不了，因此 Linux 側另寫等價的 `LocSource`；術語沿用上遊四語詞表的譯法。上遊另有「粵語」，本分支按需求只做三語。

**給翻譯貢獻者的提示**：所有可見文案集中在 `Strings.cs` 的 `Chinese` / `TraditionalChinese` / `English` 三個 `Table(...)` 裏，鍵集合必須完全一致；缺譯時 `Loc.Get` 退回簡體，再缺就回顯鍵名（便於發現漏翻）。`Loc.Format` 的 `{0}` 佔位符在三種語言裡都要保留且順序一致。

## 散佈與安裝（面向最終用戶，2026-11-26 新增）

`campfire-linux.tar.gz` 是**原始碼包**（給部署與編譯用）；這一節說的是**成品安裝包**：普通用戶從 GitHub Release 下載後直接裝，不需要 .NET SDK、不需要編譯、不需要 root。

### 提供哪幾種格式

| 格式 | Release 檔案名 | 用戶下載後 | 說明 |
|---|---|---|---|
| **AppImage**（推薦） | `Campfire-1.1.32-x86_64.AppImage` | `chmod +x` 後雙擊執行 | 單檔案、跨發行版、無沙箱，EasyTier 能正常建立虛擬網卡 |
| **tar.gz 用戶級安裝** | `campfire-1.1.32-linux-x64.tar.gz` | 解壓後 `bash install.sh` | 帶桌面圖標 / 應用菜單 / `campfire` 命令 / `uninstall.sh` |
| Flatpak（實驗性） | `release/flatpak/campfire.yml.in` | 自行用 `flatpak-builder` 建置 | ⚠ 沙箱不授予 `/dev/net/tun`，連線很可能不可用；詳見清單注釋 |

**不提供 `.deb` / `.rpm`**：Bazzite 是 rpm-ostree 不可變系統，`rpm-ostree install` 會改鏡像層並需要重啓；Debian 系包在此完全不適用。前兩種格式只寫 `$HOME`，不改動系統。

### 用戶怎麼裝

AppImage（把 OWNER/REPO 換成實際倉庫，或直接在 Release 頁面點檔案名下載）：

```bash
curl -LO https://github.com/OWNER/REPO/releases/download/v1.1.32/Campfire-1.1.32-x86_64.AppImage
chmod +x Campfire-1.1.32-x86_64.AppImage
./Campfire-1.1.32-x86_64.AppImage
```

tar.gz 用戶級安裝：

```bash
curl -LO https://github.com/OWNER/REPO/releases/download/v1.1.32/campfire-1.1.32-linux-x64.tar.gz
tar -xzf campfire-1.1.32-linux-x64.tar.gz
cd campfire-1.1.32-linux-x64
bash install.sh
```

裝完在應用菜單找 Campfire，或執行 `~/.local/bin/campfire`。`install.sh` 只寫 `~/.local`，不需要 sudo，Bazzite / Fedora Silverblue 這類不可變系統可直接用。卸載用 `bash uninstall.sh`，設定、日誌與 EasyTier 元件會保留。下載校驗：Release 裏有 `SHA256SUMS`，用 `sha256sum -c SHA256SUMS`。

### 自己怎麼建置發布包

**Windows 開發機上就能做**（.NET 8 SDK 支援交叉編譯 linux-x64，不需要 WSL / Docker）：

```bash
cd linux
bash pack-release.sh
```

產物：`linux/dist/campfire-1.1.32-linux-x64.tar.gz`（約 64 MB，含自包含執行時與 EasyTier 離線包）。設 `INCLUDE_EASYTIER=0` 可出更小的包，首次連線時應用會自動下載元件。腳本會做 ELF 頭校驗、Unix 權限位寫入、AppStream 佔位符替換、禁用內容掃描和 LF 校驗和。

**AppImage 必須在 Linux 上建置**（需要 squashfs / `appimagetool`）。倉庫裏的 `.github/workflows/release.yml` 會在 `ubuntu-22.04` runner 上完成：push 一個 `v1.1.32` 標籤，或在 Actions 裏手動觸發，CI 就自動產出 AppImage、兩種 tar.gz 與 `SHA256SUMS` 並掛到 Release —— 用戶即可從 GitHub 直接下載。單獨建置：

```bash
bash release/build-appimage.sh dist/campfire-1.1.32-linux-x64.tar.gz Campfire-1.1.32-x86_64.AppImage
```

### 合規邊界

- **絕不含** Minecraft 用戶端、Mojang 資源、Temurin JDK（版權與體積原因，見 `THIRD-PARTY-NOTICES.md`）。遊戲與 JDK 由用戶自行安裝。
- 內置 EasyTier 2.6.4 時按 **LGPL-3.0** 分發**未修改的獨立程式**，隨包附 `docs/LGPL-3.0.txt`；應用通過 `CAMPFIRE_EASYTIER_ZIP` 在首次連線時離線解壓，不必聯網。
- 本移植整體爲 GPL-3.0-or-later，包內附 `LICENSE`、`THIRD-PARTY-NOTICES.md` 與 `docs/SOURCE.txt`；**完整對應原始碼** `campfire-linux.tar.gz`（含 `src/java-agent/` 全部原始碼與 40 個 Mappings 名字表）會隨每個 Release 一同發布。

### 已知限制

- 目前只有 **linux-x64**；arm64 需把 `-r` 改成 `linux-arm64` 並另備 aarch64 版 EasyTier。
- Windows 側交叉編譯出的 ELF **沒有 Unix 執行位**，`install.sh` / `build-appimage.sh` 會 `chmod +x` 補上（打包腳本用 Python tarfile 精確寫入 0755）。
- AppImage 需要 FUSE（Bazzite 預設具備）；沒有 FUSE 時加 `--appimage-extract-and-run`。
- Flatpak 的連線限制見上表，AppImage 才是推薦格式。

## 關鍵約束

- 不碰 `../src/android/`。
- `RoomCode` / `RoomProtocol` / `RoomFraming` / `EasyTierConfig` 等協定檔案**只用鏈接、禁止副本與編輯**，否則官方 Windows 用戶端會不認。
- 大廳 ≠ 連線：邀請碼靠 EasyTier 公共節點發現 + 點對點，遊戲資料不經過任何伺服器端；砍掉大廳照樣連線。
- Java Agent 已於 2026-11-19 恢復（跨平臺 JVM attach）；CLI 仍需手填連接埠，GUI 正式連線入口可自動開放，但需完整 JDK 與真實 MC。
- 不用 Wine / Mono 跑原 exe（WPF 在 Linux 無執行時）。
- 原 Windows 工程零改動，作爲對照與回退基線。

## GPL-3.0 合規

- 每個新寫/改寫檔案頭部都標註了「修改自 / 基於 Xocio/Campfire v1.1.32 (GPL-3.0)」及修改內容；被 `<Compile Include>` 鏈接、逐字未改的檔案不加聲明。
- 分發二進制須同時提供本 fork 的**完整對應原始碼**（含 `Campfire.Cli/Link/LinkSession.cs` 改寫副本、`Campfire.Lobby/` 全部新增檔案與所有被鏈接的原 `Link\` 檔案），並保留 `../LICENSE` 與 `../THIRD-PARTY-NOTICES.md`。
- EasyTier（LGPL-3.0）保持獨立進程、未修改、按需下載；dnlib（MIT）、ASM（BSD-3-Clause）隨原工程的第三方聲明。自建大廳伺服器端不引入新的第三方 NuGet 依賴。
