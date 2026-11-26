#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「阶段 2 GUI」免剪贴板一键脚本。Bazzite + Wayland 下 VMware 拖放/剪贴板不可用，
#       所以把「更新源码包 + 离线 NuGet 包 + 还原 + 编译 + 启动 GUI」全放进本脚本，
#       虚拟机里只需手敲两条短命令，不需要复制粘贴任何长命令。
#
# 用法（Bazzite 终端，两条；IP 以 serve-for-vm.py 窗口打印的为准）：
#   curl 192.168.1.5:8000/step5.sh > step5.sh
#   bash step5.sh
#
# !! 别把 IP 套进 < > 尖括号里。bash 会把 < 当成 stdin 重定向，报
#    "bash: 192.168.1.5: 没有那个文件或目录"，curl 根本不会运行。
#    也别用 -fsS 这类 - 开头的选项，手敲漏横杠会把 fss 当成主机名。
#
# 可选覆盖：CF_HOST_IP=192.168.137.1 bash step5.sh 或 CF_PORT=8001 bash step5.sh
#
# 本脚本只写 $HOME，不改系统层、不需要 sudo。离线 NuGet 包约 167 MB，局域网传输。

set -euo pipefail

PORT="${CF_PORT:-8000}"
ROOT="$HOME/campfire-linux"
LINUX="$ROOT/linux"
GUI_DLL="$LINUX/Campfire.Avalonia/bin/Release/net8.0/Campfire.Avalonia.dll"
SRC_PKG="campfire-linux.tar.gz"
NUGET_PKG="offline-nuget.tar.gz"

echo "==== Campfire 第⑤步：Avalonia GUI（免剪贴板）===="

# ---- 1. 找到 Windows 主机上的临时 HTTP 服务 ----
HOST_IP="${CF_HOST_IP:-}"
if [ -z "$HOST_IP" ]; then
    GW="$(ip route 2>/dev/null | awk '/default/{print $3}' || true)"
    # 192.168.1.5 / 172.16.202.127 是历史上实测过的两张网卡地址；DHCP 变了就用
    # CF_HOST_IP 显式指定。$GW 覆盖 NAT 模式，192.168.1.1 等覆盖常见 VMware 网关。
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
    echo "  也可显式指定： CF_HOST_IP=192.168.1.5 bash step5.sh（换成窗口打印的 IP）。" >&2
    exit 1
fi
echo "  主机服务 : http://$HOST_IP:$PORT"

# ---- 2. 更新源码包（含新增的 Campfire.Avalonia 工程）----
echo "---- 下载并校验最新源码包 ----"
cd "$HOME"
curl -f --max-time 180 -o "$SRC_PKG" "http://$HOST_IP:$PORT/$SRC_PKG"
curl -f --max-time 30  -o "$SRC_PKG.sha256" "http://$HOST_IP:$PORT/$SRC_PKG.sha256"
sha256sum -c "$SRC_PKG.sha256"
tar -xzf "$SRC_PKG" -C "$HOME"
if [ ! -f "$LINUX/Campfire.Avalonia/Campfire.Avalonia.csproj" ]; then
    echo "✗ 源码包里没有 Campfire.Avalonia 工程，包可能还是旧版。" >&2
    exit 1
fi
echo "  源码包已更新：$ROOT"

# ---- 3. 下载并解出离线 NuGet 包 ----
# 反复调整 UI 时不用重下 167 MB：压缩包与 .sha256 都在、校验通过、且已解出目录就跳过。
echo "---- 离线 NuGet 包 ----"
NEED_NUGET=1
if [ -f "$HOME/$NUGET_PKG" ] && [ -f "$HOME/$NUGET_PKG.sha256" ] \
   && (cd "$HOME" && sha256sum -c "$NUGET_PKG.sha256" >/dev/null 2>&1) \
   && [ -d "$LINUX/offline-nuget" ]; then
    NEED_NUGET=0
    echo "  已存在且校验通过，跳过下载（省 167 MB）"
fi
if [ "$NEED_NUGET" = "1" ]; then
    echo "  下载约 167 MB，请等一会儿"
    curl -f --max-time 600 -o "$HOME/$NUGET_PKG" "http://$HOST_IP:$PORT/$NUGET_PKG"
    curl -f --max-time 30  -o "$HOME/$NUGET_PKG.sha256" "http://$HOST_IP:$PORT/$NUGET_PKG.sha256"
    (cd "$HOME" && sha256sum -c "$NUGET_PKG.sha256")
    tar -xzf "$HOME/$NUGET_PKG" -C "$LINUX"
fi
if [ ! -d "$LINUX/offline-nuget" ]; then
    echo "✗ 离线包解压后没找到 offline-nuget/ 目录。" >&2
    exit 1
fi
echo "  离线包就绪：$(find "$LINUX/offline-nuget" -name '*.nupkg' | wc -l) 个 .nupkg"

# ---- 4. 运行时生成 NuGet.Config（不入库，<clear/> 会挡住 Windows 侧联网还原）----
cat > "$LINUX/NuGet.Config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline" value="./offline-nuget" />
  </packageSources>
</configuration>
EOF

# ---- 5. 还原 + 全量编译 ----
if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "✗ 没找到 dotnet；先跑 bootstrap.sh --install-sdk。" >&2
    exit 1
fi
echo "---- dotnet restore（只用离线源，不连 nuget.org）----"
dotnet restore "$LINUX/Campfire.Avalonia/Campfire.Avalonia.csproj" --source "$LINUX/offline-nuget"
echo "---- 全量编译 + 11 项协议自检 + doctor ----"
bash "$LINUX/build.sh"

if [ ! -f "$GUI_DLL" ]; then
    echo "✗ 编译完成但没找到 $GUI_DLL" >&2
    exit 1
fi

# ---- 6. 图形环境检查 ----
echo "==== 图形环境 ===="
echo "  DISPLAY=${DISPLAY:-<空>}   XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-<空>}"
if [ -z "${DISPLAY:-}" ]; then
    echo "  ⚠ DISPLAY 为空：Bazzite GNOME 通常走 XWayland，若启动失败可试 export DISPLAY=:0"
fi
if command -v fc-list >/dev/null 2>&1; then
    if fc-list :lang=zh > "$HOME/cf-fonts.txt" 2>/dev/null && [ -s "$HOME/cf-fonts.txt" ]; then
        echo "  中文字体   : 已安装"
    else
        echo "  ⚠ 未检测到中文字体，界面文字可能显示方块（无 sudo 无法安装）"
    fi
fi

# 短启动入口：默认跳过世界探测（虚拟机没装 MC 时只验房间层），
# 正式联机前把 1 改成 0，并先在 MC 里点「对局域网开放」。
cat > "$HOME/campfire-gui.sh" <<EOF
#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：GUI 短启动入口，避免每次手敲一长串 dotnet 路径。
export CAMPFIRE_SKIP_PROBE=1
exec dotnet "$GUI_DLL" "\$@"
EOF
chmod +x "$HOME/campfire-gui.sh"

# 正式联机入口：不跳过世界探测，并默认勾上「自动开放局域网世界」。
# 需要真实 Minecraft + 带 jdk.attach 的完整 JDK；缺哪个日志里都会写明。
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
echo "==== 第⑤步完成，正在启动 GUI（Ctrl+C 结束）===="
echo "  房间层自检（无 MC）： bash ~/campfire-gui.sh"
echo "  正式联机+自动开局域网： bash ~/campfire-gui-autolan.sh"
echo "  若透明窗口异常，追加环境变量 XLIB_SKIP_ARGB_VISUALS=1"
echo ""
exec dotnet "$GUI_DLL"
