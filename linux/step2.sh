#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「免剪贴板」一键脚本。VMware 的拖放 / 剪贴板共享依赖客户机
#       open-vm-tools-desktop 的 vmware-user，Bazzite（不可变系统 + Wayland）上不可用，
#       主机复制的内容同步不进虚拟机，粘贴只会得到缓存的旧命令。
#       因此把第②步（EasyTier 2.6.4 下载 → 校验 → 安装 → 检查 → doctor）全部放进本脚本，
#       虚拟机里只需手敲一行 curl | bash，不需要复制粘贴任何长命令。
#
# 用法（Bazzite 终端，一行）：
#   curl -fsS 172.16.202.127:8000/step2.sh | bash
# 可选覆盖： CF_HOST_IP=<主机IP> 或 CF_PORT=<端口>，例如
#   curl -fsS 172.16.202.127:8000/step2.sh | CF_HOST_IP=192.168.137.1 bash
#
# 本脚本只写 $HOME 下的 easytier zip、.bashrc 与 campfire 数据目录，不改系统层、不需要 sudo。

set -euo pipefail

PORT="${CF_PORT:-8000}"
ZIP="easytier-linux-x86_64-v2.6.4.zip"
ROOT="$HOME/campfire-linux"
CLI_DLL="$ROOT/linux/Campfire.Cli/bin/Release/net8.0/campfire.dll"
BRC="$HOME/.bashrc"

echo "==== Campfire 第②步：EasyTier 2.6.4（免剪贴板）===="

# ---- 1. 找到 Windows 主机上的临时 HTTP 服务 ----
HOST_IP="${CF_HOST_IP:-}"
if [ -z "$HOST_IP" ]; then
    GW="$(ip route 2>/dev/null | awk '/default/{print $3}' || true)"
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
    echo "  若主机 IP 不是 172.16.202.127，用 CF_HOST_IP=<IP> 指定。" >&2
    exit 1
fi
echo "  主机服务 : http://$HOST_IP:$PORT"

# ---- 2. 前置检查（避免又报「没有那个文件或目录」）----
if [ ! -f "$ROOT/bootstrap.sh" ]; then
    echo "✗ 找不到 $ROOT/bootstrap.sh —— 源码包还没解压到家目录？" >&2
    exit 1
fi
if [ ! -f "$CLI_DLL" ]; then
    echo "✗ 找不到 $CLI_DLL" >&2
    echo "  bootstrap 可能没跑完，先在终端执行： cd ~/campfire-linux && bash bootstrap.sh --install-sdk" >&2
    exit 1
fi
if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "✗ 没找到 dotnet（~/.dotnet 下也没有），先跑 bash bootstrap.sh --install-sdk" >&2
    exit 1
fi
echo "  dotnet   : $(dotnet --version)"
echo "  工程     : $CLI_DLL"

# ---- 3. 把 PATH / alias 写进 .bashrc（幂等；以后开新终端可直接敲 campfire）----
grep -qF 'export PATH="$HOME/.dotnet:$PATH"' "$BRC" 2>/dev/null || \
    echo 'export PATH="$HOME/.dotnet:$PATH"' >> "$BRC"
grep -qF 'alias campfire=' "$BRC" 2>/dev/null || \
    echo 'alias campfire="dotnet $HOME/campfire-linux/linux/Campfire.Cli/bin/Release/net8.0/campfire.dll"' >> "$BRC"

cf() { dotnet "$CLI_DLL" "$@"; }

# ---- 4. 下载 + 校验（局域网直连；断了直接重跑本脚本，会覆盖重下）----
cd "$HOME"
echo "  下载 EasyTier（约 25 MB，走局域网）…"
curl -fLsS --retry 3 --max-time 180 -o "$ZIP" "http://$HOST_IP:$PORT/$ZIP"
curl -fLsS --retry 3 --max-time 30  -o "$ZIP.sha256" "http://$HOST_IP:$PORT/$ZIP.sha256"
sha256sum -c "$ZIP.sha256"

# ---- 5. 安装 + 检查 ----
echo "  安装到 campfire 数据目录…"
if ! cf easytier install --local "$HOME/$ZIP"; then
    echo "✗ 安装失败，把上面的报错发给我。" >&2
    exit 1
fi
echo ""
echo "==== easytier check ===="
cf easytier check || echo "  ⚠ easytier check 未通过"
echo ""
echo "==== doctor ===="
cf doctor || echo "  ⚠ doctor 仍有红项"
echo ""
echo "==== 第②步跑完 ===="
echo "把上面 easytier check 与 doctor 的输出发给我。"
echo "之后开一个新终端（会自动加载 alias），就能直接敲 campfire 了。"
