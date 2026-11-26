#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 作用：在 Linux 上解压源码包后，一键检查环境、修权限、编译两个工程、跑自检。
#
# 用法（在解压出来的 campfire-linux/ 目录里）：
#   bash bootstrap.sh                 # 已有 dotnet 时
#   bash bootstrap.sh --install-sdk   # 顺带把 .NET 8 SDK 装到 ~/.dotnet（不需要 sudo）
#
# 本脚本不会自动 sudo、不会改系统目录；SDK 只装在用户目录，删掉 ~/.dotnet 即可卸载。

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LINUX="$HERE/linux"

echo "==== 1/5 系统信息 ===="
echo "  内核/架构: $(uname -srm)"
echo "  发行版   : $( ( . /etc/os-release 2>/dev/null && echo "$PRETTY_NAME" ) || echo '未知' )"
echo "  工作目录 : $HERE"

if [ "${1:-}" = "--install-sdk" ]; then
    echo ""
    echo "==== 安装 .NET 8 SDK 到 $HOME/.dotnet（约 200 MB，支持断点续传）===="
    if ! command -v wget >/dev/null 2>&1; then
        echo "需要 wget：sudo apt install -y wget" >&2
        exit 1
    fi
    wget -c https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$PATH"
    echo "  已加入本次会话的 PATH；新开终端请执行：export PATH=\"\$HOME/.dotnet:\$PATH\""
fi

echo ""
echo "==== 2/5 检查 .NET 8 SDK ===="
if ! command -v dotnet >/dev/null 2>&1; then
    cat >&2 <<'EOF'
未找到 dotnet。三选一安装 .NET 8 SDK 后重跑本脚本：

  # A. Ubuntu 24.04（最简单）
  sudo apt update && sudo apt install -y dotnet-sdk-8.0

  # B. 官方脚本，装到用户目录，不需要 sudo
  wget -c https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"

  # C. 让本脚本代做 B
  bash bootstrap.sh --install-sdk

学校网慢时也可在 Windows 上用迅雷下好 dotnet-sdk-8.0.xxx-linux-x64.tar.gz，
再用 U 盘拷过来手动解压到 ~/.dotnet。
EOF
    exit 1
fi
VER="$(dotnet --version)"
echo "  dotnet --version = $VER"
case "$VER" in
    8.*) echo "  SDK 版本 OK" ;;
    *)   echo "  警告：项目目标框架为 net8.0，当前是 $VER，可能需要 .NET 8 SDK。" >&2 ;;
esac

echo ""
echo "==== 3/5 修换行符与可执行位 ===="
find "$LINUX" -type f -name '*.sh' -print0 | xargs -0 -r sed -i 's/\r$//'
chmod +x "$LINUX"/*.sh
echo "  $(find "$LINUX" -maxdepth 1 -name '*.sh' | wc -l) 个脚本已就绪"

echo ""
echo "==== 4/5 目录结构检查（最容易漏的一条）===="
MISSING=0
for p in \
    "$HERE/src/Campfire/Hex.cs" \
    "$HERE/src/Campfire/Link/RoomCode.cs" \
    "$LINUX/Campfire.Cli/Campfire.Cli.csproj" \
    "$LINUX/Campfire.Lobby/Campfire.Lobby.csproj" \
    "$LINUX/Campfire.Avalonia/Campfire.Avalonia.csproj" \
    "$HERE/LICENSE" \
    "$HERE/THIRD-PARTY-NOTICES.md"; do
    if [ -f "$p" ]; then
        echo "  OK   ${p#$HERE/}"
    else
        echo "  缺失 ${p#$HERE/}" >&2
        MISSING=1
    fi
done
if [ "$MISSING" -ne 0 ]; then
    cat >&2 <<'EOF'

目录结构被破坏了。两个 .csproj 通过 ../../src/Campfire/ 链接原协议层，
必须保持下面这种同级结构（只拷 linux/ 目录过来一定会编译失败）：

  campfire-linux/
  ├── src/Campfire/Hex.cs
  ├── src/Campfire/Link/**/*.cs
  └── linux/{Campfire.Cli,Campfire.Lobby,Campfire.Avalonia,*.sh}

请重新用完整包解压，或按 README「搬到 Linux 机器上」一节补齐 src/。
EOF
    exit 1
fi

echo ""
echo "==== 5/5 构建两个工程 + 11 项协议自检 + 环境自检 ===="
bash "$LINUX/build.sh"

CLI_DLL="$LINUX/Campfire.Cli/bin/Release/net8.0/campfire.dll"
cat <<EOF

==== 初始化完成 ====

为命令起个短名（可选，但强烈建议），复制这一行执行：
  echo "alias campfire='dotnet $CLI_DLL'" >> ~/.bashrc && source ~/.bashrc

不想设 alias 也行，每条命令写成：
  dotnet $CLI_DLL <命令>

接下来：

  # 1. 下载并安装 EasyTier 2.6.4 Linux 组件（约 25 MB，支持断点续传）
  bash linux/fetch-easytier.sh
  campfire easytier install --local ./easytier-linux-x86_64-v2.6.4.zip
  campfire easytier check

  # 2. 可选：启动自建大厅并跑 42 项契约验收（两个终端）
  bash linux/run-lobby.sh
  bash linux/verify-lobby.sh

  # 3. 最后的硬标准：与官方 Windows Campfire.exe 双向用邀请码互进
  #    详见 linux/README.md「后续阶段」。
EOF
