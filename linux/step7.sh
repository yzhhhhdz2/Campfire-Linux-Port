#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「第⑦步：装 Minecraft Java 26.3 + Java 25，跑自动开放局域网端到端实测」。
#       26.3 是 2026-09-15 的正式版（Wilderness Bound）；26.1 起 Mojang 取消混淆，
#       所以本版本不需要 client_mappings，Java Agent 直接按官方名反射即可。
#       但 26.1 起游戏要求 Java 25（class file 69），step6.sh 装的 JDK 17 只够旧版 MC；
#       这里让游戏与 attach 用同一个 JDK 25，避免跨大版本 attach 失败。
#       Bazzite 是不可变系统，全程只写 $HOME/.local，不 sudo、不改系统层。
#
# 用法（Bazzite 终端，一行；IP 以服务窗口打印的为准）：
#   curl 192.168.1.5:8000/step7.sh | bash
#
# !! 别把 IP 套进尖括号（bash 会当成 stdin 重定向），
#    也别用 -fsS 这类以横杠开头的选项组合（手敲漏横杠会把 fss 当主机名）。
# 可选覆盖：CF_HOST_IP=192.168.137.1 bash step7.sh 或 CF_PORT=8001 bash step7.sh

set -euo pipefail

PORT="${CF_PORT:-8000}"
MC_ASSET="mc-26.3-offline.tar.gz"
JDK_ASSET="OpenJDK25U-jdk_x64_linux_hotspot_25.0.5_8.tar.gz"
MC_ROOT="$HOME/.local/campfire-mc263"
MC="$MC_ROOT/mc-26.3"
JDK_ROOT="$HOME/.local/campfire-jdk25"
JAVA="$JDK_ROOT/bin/java"
GUI_DLL="$HOME/campfire-linux/linux/Campfire.Avalonia/bin/Release/net8.0/Campfire.Avalonia.dll"

echo "==== Campfire 第⑦步：Minecraft 26.3 + Java 25（自动开放局域网实测）===="

# ---- 1. 找到 Windows 主机上的临时 HTTP 服务 ----
HOST_IP="${CF_HOST_IP:-}"
if [ -z "$HOST_IP" ]; then
    GW="$(ip route 2>/dev/null | awk '/default/{print $3}' || true)"
    # 默认网关覆盖 NAT；后两个是历史上实测过的两张网卡地址，DHCP 变了就用 CF_HOST_IP 指定。
    for ip in $GW 192.168.1.5 172.16.202.127 192.168.1.1 192.168.137.1 192.168.220.1; do
        if curl -fsS --max-time 4 -o /dev/null "http://$ip:$PORT/" 2>/dev/null; then
            HOST_IP="$ip"
            break
        fi
    done
fi
if [ -z "$HOST_IP" ]; then
    echo "✗ 没找到主机上的 HTTP 服务（端口 $PORT）。" >&2
    echo "  确认 Windows 那个 serve-for-vm.py 窗口还开着；" >&2
    echo "  也可显式指定： CF_HOST_IP=192.168.1.5 bash step7.sh" >&2
    exit 1
fi
echo "  主机服务 : http://$HOST_IP:$PORT"

fetch_asset() {
    local asset="$1"
    if [ -f "$HOME/$asset" ] && [ -f "$HOME/$asset.sha256" ] \
       && (cd "$HOME" && sha256sum -c "$asset.sha256" >/dev/null 2>&1); then
        echo "  $asset : 已下载且校验通过，跳过。"
        return 0
    fi
    echo "---- 下载 $asset（局域网，请等一会儿）----"
    curl -f --max-time 900 -o "$HOME/$asset" "http://$HOST_IP:$PORT/$asset"
    curl -f --max-time 60  -o "$HOME/$asset.sha256" "http://$HOST_IP:$PORT/$asset.sha256"
    (cd "$HOME" && sha256sum -c "$asset.sha256")
}

fetch_asset "$MC_ASSET"
fetch_asset "$JDK_ASSET"

# ---- 2. 解压游戏（可重复执行：有 classpath.txt 就认为装好了）----
if [ -f "$MC/classpath.txt" ]; then
    echo "  游戏文件 : 已解压，跳过。"
else
    echo "---- 解压 Minecraft 26.3 到 $MC_ROOT ----"
    mkdir -p "$MC_ROOT"
    tar -xzf "$HOME/$MC_ASSET" -C "$MC_ROOT"
fi
if [ ! -f "$MC/classpath.txt" ]; then
    echo "✗ 没找到 $MC/classpath.txt，游戏包可能不完整。" >&2
    exit 1
fi

# ---- 2b. classpath 自检 ----
# 旧版离线包在 Windows 上生成 classpath.txt 时用过 pathlib.Path 与 write_text，
# 会留下反斜杠分隔符和 CRLF；Linux 的 -cp 不认，表现为「找不到或无法加载主类」。
# 这里就地重建，用户不必重下 497 MB 的包。
if grep -qF '\' "$MC/classpath.txt" || grep -q $'\r' "$MC/classpath.txt"; then
    echo "  classpath.txt 含 Windows 分隔符或 CR，自动重建…"
    (
        cd "$MC"
        CP="$(find libraries -type f -name '*.jar' | sort | tr '\n' ':')versions/26.3/26.3.jar"
        printf '%s\n' "$CP" > "$MC/classpath.txt"
    )
fi

# ---- 3. 解压 JDK 25（顶层 jdk-25.0.5+8/，去掉一层让 bin/java 路径固定）----
if [ -x "$JAVA" ] && "$JAVA" --list-modules 2>/dev/null | grep -q "jdk.attach@"; then
    echo "  JDK 25   : 已安装，跳过解压。"
else
    echo "---- 解压 JDK 25 到 $JDK_ROOT ----"
    mkdir -p "$JDK_ROOT"
    tar -xzf "$HOME/$JDK_ASSET" -C "$JDK_ROOT" --strip-components=1
fi
if [ ! -x "$JAVA" ]; then
    echo "✗ 没找到 $JAVA，解压可能失败了。" >&2
    exit 1
fi

echo "==== 环境自检 ===="
"$JAVA" -version 2>&1 | head -3
if ! "$JAVA" --list-modules 2>/dev/null | grep -q "jdk.attach@"; then
    echo "✗ 这个 java 没有 jdk.attach 模块，不能注入 Java Agent。" >&2
    exit 1
fi
echo "  jdk.attach : $("$JAVA" --list-modules 2>/dev/null | grep 'jdk.attach@' | head -1)"
echo "  游戏 jar   : $("$JAVA" -version 2>&1 | head -1) 跑 26.3（class file 69）"
echo "  资产对象   : $(find "$MC/assets/objects" -type f | wc -l) 个"

# ---- 3b. 低画质设置（2026-11-25 真机验证：VMware 软件渲染下必须）----
GAME_DIR="${MC_GAME_DIR:-$HOME/.minecraft}"
mkdir -p "$GAME_DIR"
if [ -f "$GAME_DIR/options.txt" ]; then
    cp "$GAME_DIR/options.txt" "$GAME_DIR/options.txt.bak-campfire"
fi
cat > "$GAME_DIR/options.txt" <<'OPTIONS'
renderDistance:2
simulationDistance:2
guiScale:1
maxFps:30
enableVsync:false
fancyGraphics:false
clouds:0
particles:0
entityShadows:false
ao:false
fullscreen:false
gamma:1.0
OPTIONS

# ---- 4. 生成离线启动脚本（不需要启动器、不需要正版账号）----
cat > "$HOME/minecraft-26.3.sh" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：直接按 Mojang 版本 json 拼 classpath 启动 26.3 客户端，跳过启动器与账号登录。
#       2026-11-25 真机确认：VMware 软件渲染下必须走 X11 + llvmpipe + 低画质，
#       否则 26.3 (SDL3) 只会画出主菜单全景、UI 不渲染。要试 Wayland/Vulkan 用 step7-gfx.sh。
#       只用于「进入单人世界 → 自动开放局域网」的实测；游戏归 Mojang/Microsoft 所有。
set -euo pipefail
MC="$MC"
JAVA="$JAVA"
GAME_DIR="\${MC_GAME_DIR:-\$HOME/.minecraft}"
USER_NAME="\${MC_USER:-CampfireHost}"
USER_TYPE="\${MC_USER_TYPE:-msa}"
export SDL_VIDEODRIVER="\${MC_SDL_DRIVER:-x11}"
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe
mkdir -p "\$GAME_DIR" "\$MC/natives/jna" "\$MC/natives/lwjgl" "\$MC/natives/netty"
cd "\$MC"
CP="\$(cat "\$MC/classpath.txt")"
# 主菜单 UI 出不来时，可用 MC_QUICK_WORLD=<saves 里的文件夹名> 直接进世界。
EXTRA=()
if [ -n "\${MC_QUICK_WORLD:-}" ]; then
    EXTRA=(--quickPlaySingleplayer "\${MC_QUICK_WORLD}")
fi
exec "\$JAVA" \\
    -Xmx1G -XX:StackShadowPages=32 \\
    --enable-native-access=ALL-UNNAMED \\
    --add-exports java.base/jdk.internal.misc=ALL-UNNAMED \\
    "-Djava.library.path=\$MC/natives/java" \\
    "-Djna.tmpdir=\$MC/natives/jna" \\
    "-Dorg.lwjgl.system.SharedLibraryExtractPath=\$MC/natives/lwjgl" \\
    "-Dio.netty.native.workdir=\$MC/natives/netty" \\
    "-Dminecraft.launcher.brand=Campfire" \\
    "-Dminecraft.launcher.version=1.1.32" \\
    -cp "\$CP" net.minecraft.client.main.Main \\
    --username "\$USER_NAME" --version 26.3 --versionType release \\
    --gameDir "\$GAME_DIR" --assetsDir "\$MC/assets" --assetIndex 34 \\
    --uuid 00000000-0000-0000-0000-000000000001 \\
    --accessToken 0 --clientId 0 --xuid 0 --userType "\$USER_TYPE" \\
    --width 800 --height 600 "\${EXTRA[@]}"
EOF
chmod +x "$HOME/minecraft-26.3.sh"

# ---- 5. 让 Campfire 用同一个 JDK 25 跑游戏与 attach ----
cat > "$HOME/campfire-jdk25-env.sh" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：Minecraft 26.x 需要 Java 25；游戏与 Java Agent attach 共用这一个完整 JDK。
export CAMPFIRE_ATTACH_JAVA="$JAVA"
export JAVA_HOME="$JDK_ROOT"
EOF
chmod +x "$HOME/campfire-jdk25-env.sh"

cat > "$HOME/campfire-gui-autolan-26.sh" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：26.3 专用的正式联机入口（启用世界探测 + 自动开放 + JDK 25）。
unset CAMPFIRE_SKIP_PROBE
export CAMPFIRE_AUTO_LAN=1
. "\$HOME/campfire-jdk25-env.sh"
exec dotnet "$GUI_DLL" "\$@"
EOF
chmod +x "$HOME/campfire-gui-autolan-26.sh"

echo ""
echo "==== 第⑦步完成 ===="
if [ -f "$GUI_DLL" ]; then
    echo "  GUI 已编译。"
else
    echo "  ⚠ 还没编译 GUI（$GUI_DLL 不存在），先跑第⑤步再启动。"
fi
echo ""
echo "---- 端到端实测：开两个终端 ----"
echo "  终端 A： bash ~/minecraft-26.3.sh"
echo "           等进主菜单 → 单人游戏 → 新建世界 → 进入世界（保持窗口别关）"
echo "  终端 B： bash ~/campfire-gui-autolan-26.sh"
echo "           GUI 主页点「创建房间」→「自动开放」已默认勾上 → 点「开始」"
echo ""
echo "---- 判据 ----"
echo "  GUI 工具页日志出现：Java agent loaded into game process / Opened <世界名> to LAN on port N"
echo "  房间状态不再停在「等待世界端口」，Windows 官方客户端能加入并看到 2 人。"
echo ""
echo "---- 排错 ----"
echo "  游戏起不来：看终端 A 报错；认证相关可试 MC_USER_TYPE=legacy bash ~/minecraft-26.3.sh"
echo "  启动脚本已按真机验证的低配参数生成（X11 + llvmpipe + 800x600 + 低画质）。"
echo "  仍卡在只有背景没有按钮：跑 step7-gfx.sh 做诊断，并生成 Vulkan 备选脚本。"
echo "  GUI 找不到游戏： pgrep -a java  确认游戏进程在；两个终端要同时开着。"
echo "  26.3 无混淆，不需要 client_mappings；映射表相关日志属于正常降级。"
echo "  旧版 MC（1.14.4–1.21.11）仍用 step6.sh 的 JDK 17 与 campfire-gui-autolan.sh，互不影响。"
