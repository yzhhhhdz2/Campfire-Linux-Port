#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「安装带 jdk.attach 的完整 JDK」免剪贴板一键脚本。
#       Bazzite 是不可变系统（不能 dnf install），MC 启动器自带的多为 JRE，
#       没有 jdk.attach 模块 → Java Agent 无法注入游戏自动开放局域网世界。
#       JDK 由 Windows 侧 fetch-jdk.sh 预下好，经 serve-for-vm.py 的 HTTP 通道传来，
#       本脚本只写 $HOME/.local，不动系统层、不需要 sudo。
#
# 用法（Bazzite 终端，一行；IP 以服务窗口打印的为准）：
#   curl 192.168.1.5:8000/step6.sh | bash
#
# !! 别把 IP 套进 < > 尖括号（bash 会当成 stdin 重定向），
#    也别用 -fsS 这类 - 开头选项（手敲漏横杠会把 fss 当主机名）。
# 可选覆盖：CF_HOST_IP=192.168.137.1 bash step6.sh 或 CF_PORT=8001 bash step6.sh

set -euo pipefail

PORT="${CF_PORT:-8000}"
ASSET="OpenJDK17U-jdk_x64_linux_hotspot_17.0.21_9.tar.gz"
JDK_ROOT="$HOME/.local/campfire-jdk"
JAVA="$JDK_ROOT/bin/java"
LINUX="$HOME/campfire-linux/linux"
GUI_DLL="$LINUX/Campfire.Avalonia/bin/Release/net8.0/Campfire.Avalonia.dll"

echo "==== Campfire 第⑥步：安装完整 JDK（自动开放局域网的前置条件）===="

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
    echo "  也可显式指定： CF_HOST_IP=192.168.1.5 bash step6.sh" >&2
    exit 1
fi
echo "  主机服务 : http://$HOST_IP:$PORT"

# ---- 2. 下载（已装好且校验通过就跳过，省 185 MB）----
NEED=1
if [ -f "$HOME/$ASSET" ] && [ -f "$HOME/$ASSET.sha256" ] \
   && (cd "$HOME" && sha256sum -c "$ASSET.sha256" >/dev/null 2>&1) \
   && [ -x "$JAVA" ] \
   && "$JAVA" --list-modules 2>/dev/null | grep -q "jdk.attach@"; then
    NEED=0
fi

if [ "$NEED" = "1" ]; then
    echo "---- 下载并校验 JDK（约 185 MB，局域网，请等一会儿）----"
    curl -f --max-time 600 -o "$HOME/$ASSET" "http://$HOST_IP:$PORT/$ASSET"
    curl -f --max-time 30  -o "$HOME/$ASSET.sha256" "http://$HOST_IP:$PORT/$ASSET.sha256"
    (cd "$HOME" && sha256sum -c "$ASSET.sha256")

    echo "---- 解压到 $JDK_ROOT ----"
    mkdir -p "$JDK_ROOT"
    # Temurin tarball 顶层是 jdk-17.0.21+9/，去掉一层让 bin/java 路径固定。
    tar -xzf "$HOME/$ASSET" -C "$JDK_ROOT" --strip-components=1
else
    echo "  已安装且校验通过，跳过下载与解压。"
fi

# ---- 3. 验证这个 java 真的能跑、且带 attach 模块 ----
if [ ! -x "$JAVA" ]; then
    echo "✗ 没找到 $JAVA，解压可能失败了。" >&2
    exit 1
fi
echo "==== JDK 自检 ===="
"$JAVA" -version 2>&1 | head -3
if ! "$JAVA" --list-modules 2>/dev/null | grep -q "jdk.attach@"; then
    echo "✗ 这个 java 没有 jdk.attach 模块，不能用于注入 Java Agent。" >&2
    exit 1
fi
echo "  jdk.attach : $("$JAVA" --list-modules 2>/dev/null | grep 'jdk.attach@' | head -1)"
echo "  CAMPFIRE_ATTACH_JAVA : $JAVA"

# ---- 4. 写环境片段 + 重写正式联机入口 ----
cat > "$HOME/campfire-jdk-env.sh" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：让 Campfire 找到带 jdk.attach 的完整 JDK。由 step6.sh 生成，可被 source。
export CAMPFIRE_ATTACH_JAVA="$JAVA"
export JAVA_HOME="$JDK_ROOT"
EOF
chmod +x "$HOME/campfire-jdk-env.sh"

# 与 step5.sh 生成的入口保持同一模板，只是多了 source 环境片段。
cat > "$HOME/campfire-gui-autolan.sh" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：正式联机入口，启用 Java Agent 自动开放局域网世界。
unset CAMPFIRE_SKIP_PROBE
export CAMPFIRE_AUTO_LAN=1
# step6.sh 装好完整 JDK 后会写这个片段；没装就跳过，行为与以前一致。
if [ -f "\$HOME/campfire-jdk-env.sh" ]; then
    . "\$HOME/campfire-jdk-env.sh"
fi
exec dotnet "$GUI_DLL" "\$@"
EOF
chmod +x "$HOME/campfire-gui-autolan.sh"

echo ""
echo "==== 第⑥步完成 ===="
if [ -f "$GUI_DLL" ]; then
    echo "  正式联机+自动开局域网： bash ~/campfire-gui-autolan.sh"
else
    echo "  ⚠ 还没编译 GUI（$GUI_DLL 不存在），先跑第⑤步再启动。"
fi
echo "  房间层自检（无 MC）：     bash ~/campfire-gui.sh"
echo ""
echo "  还差一件事才能真正看到自动开放："
echo "    虚拟机里装真实 Minecraft 并进入单人世界（假世界桩没有 publishServer 可调）。"
echo "  混淆版 MC（1.14.4–1.21.11）的映射表会自动从 BMCLAPI/Mojang 下载并按 SHA-1 缓存；"
echo "  若虚拟机连不上，日志会写明，也可把预下好的、以 SHA-1 命名的 .txt 放进数据目录的 mappings/ 子目录。"
echo "  失败不会卡死：日志会写明原因，取消勾选、手填端口照样能联机。"
echo ""
echo "  ⚠ 本脚本装的是 JDK 17，只够 1.18–1.20.4 以及旧版 MC 的 attach 端。"
echo "    Minecraft 26.1 起要求 Java 25（class file 69），26.1 起也不再混淆、无需映射表。"
echo "    装 26.3 请改跑第⑦步 step7.sh（Temurin 25 + 离线游戏包 + 26 专用联机入口），"
echo "    本脚本生成的旧入口保持不变，两者互不影响。"
