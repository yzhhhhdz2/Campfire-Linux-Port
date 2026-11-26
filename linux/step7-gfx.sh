#!/usr/bin/env bash
# Campfire Linux port — 基于 Xocio/Campfire v1.1.32 (GPL-3.0)，新增文件（非改写）。
# 作用：诊断 Minecraft 26.3 在虚拟机里「画面停在模糊背景、UI 不出现」；生成两个低配启动脚本。
#
# 为什么：26.3 把窗口/输入库从 GLFW 换成 SDL3，Wayland 独占全屏有已知崩溃；
#         26.2 起又引入 Vulkan 渲染后端（--graphicsBackend，26.3 正式版默认仍是 OpenGL）。
#         VMware SVGA + llvmpipe 软件渲染下，默认 OpenGL 路径容易把 UI 层卡住。
#         这里改用 XWayland + 强制 llvmpipe + 小窗口 + 低画质，并给一个 Vulkan 备选。
#
# 用法（Bazzite 终端，一行；IP 以服务窗口打印的为准）：
#   curl 192.168.1.5:8000/step7-gfx.sh | bash
# !! 别把 IP 套进尖括号，也别用以横杠开头的选项组合。

set -euo pipefail

MC="$HOME/.local/campfire-mc263/mc-26.3"
JAVA="$HOME/.local/campfire-jdk25/bin/java"
GAME_DIR="${MC_GAME_DIR:-$HOME/.minecraft}"

echo "==== 26.3 卡画面：诊断 ===="

echo "---- 1. java 进程 ----"
pgrep -a java || echo "  没有 java 进程（游戏已退出？）"

echo "---- 2. latest.log 尾部 25 行（这段请截图给我）----"
if [ -f "$GAME_DIR/logs/latest.log" ]; then
    tail -n 25 "$GAME_DIR/logs/latest.log"
else
    echo "  没有 $GAME_DIR/logs/latest.log"
fi

echo "---- 3. 已有世界 ----"
if [ -d "$GAME_DIR/saves" ]; then
    ls -1 "$GAME_DIR/saves" | head -10
else
    echo "  没有 saves 目录（说明还没成功创建/进入世界）"
fi

echo "---- 4. options.txt ----"
if [ -f "$GAME_DIR/options.txt" ]; then
    head -n 10 "$GAME_DIR/options.txt"
else
    echo "  不存在（首次启动）"
fi

echo "---- 5. GL / Vulkan 环境 ----"
if command -v glxinfo >/dev/null 2>&1; then
    glxinfo -B 2>/dev/null | grep -E "OpenGL renderer|OpenGL version" | head -3
else
    echo "  没装 glxinfo，跳过"
fi
if [ -d /usr/share/vulkan/icd.d ]; then
    echo "  Vulkan ICD: $(ls -1 /usr/share/vulkan/icd.d | head -4 | tr '\n' ' ')"
else
    echo "  没找到 Vulkan ICD（Vulkan 备选可能不可用）"
fi

# ---- 备份并写低画质设置 ----
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
echo "  已写低画质 options.txt（原文件备份为 options.txt.bak-campfire）"

# ---- 生成低配启动脚本 ----
write_launch() {
    local out="$1" backend="$2" env_lines="$3"
    cat > "$out" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 基于 Xocio/Campfire v1.1.32 (GPL-3.0)，新增文件（非改写）。
# 26.3 在 VMware 软件渲染下的低配启动（后端: $backend）。由 step7-gfx.sh 生成。
set -euo pipefail
MC="$MC"
JAVA="$JAVA"
GAME_DIR="\${MC_GAME_DIR:-\$HOME/.minecraft}"
USER_NAME="\${MC_USER:-CampfireHost}"
USER_TYPE="\${MC_USER_TYPE:-msa}"
$env_lines
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
    --width 800 --height 600 $backend "\${EXTRA[@]}"
EOF
    chmod +x "$out"
}

COMMON_ENV='export SDL_VIDEODRIVER=x11
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe'

# 低配版不加 --graphicsBackend，沿用 26.3 正式版默认的 OpenGL，
# 避免参数名在这个版本上有出入；Vulkan 版才显式切换（26.2 起支持）。
write_launch "$HOME/minecraft-26.3-low.sh" "" "$COMMON_ENV"
write_launch "$HOME/minecraft-26.3-vk.sh" "--graphicsBackend vulkan" "$COMMON_ENV"

echo ""
echo "==== 生成完毕 ===="
echo "  低配 OpenGL : bash \$HOME/minecraft-26.3-low.sh   （先试这个）"
echo "  Vulkan 备选 : bash \$HOME/minecraft-26.3-vk.sh    （上一个还卡再试）"
echo ""
echo "步骤："
echo "  1. 在卡住的那个终端按 Ctrl+C 结束游戏"
echo "  2. 跑低配脚本，等主菜单按钮出现（软件渲染可能要等 1–2 分钟）"
echo "  3. 进单人世界后别关，另一个终端跑 bash \$HOME/campfire-gui-autolan-26.sh"
echo ""
echo "如果按钮还是不出来但上面第 3 段 saves 里已有世界："
echo "  MC_QUICK_WORLD=世界文件夹名 bash \$HOME/minecraft-26.3-low.sh"
echo "  用 --quickPlaySingleplayer 跳过菜单直接进世界，照样能测自动开放局域网。"
echo "  还不行就把第 2 段 latest.log 的尾部截图发我。"
