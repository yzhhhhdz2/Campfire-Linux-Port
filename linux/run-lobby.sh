#!/usr/bin/env bash
# Campfire Linux port — 启动自建联机大厅服务端（Xocio/Campfire v1.1.32, GPL-3.0）。
# 前置：先跑 ./build.sh。默认监听 http://127.0.0.1:5080（见 Campfire.Lobby/appsettings.json）。
# 覆盖监听地址：./run-lobby.sh --urls http://0.0.0.0:5080
# 覆盖配置：Lobby__AdminToken=xxx ./run-lobby.sh

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOTNET="${DOTNET:-dotnet}"
DLL="$HERE/Campfire.Lobby/bin/Release/net8.0/campfire-lobby.dll"

if [ ! -f "$DLL" ]; then
    echo "找不到 $DLL" >&2
    echo "先在 linux/ 目录跑 ./build.sh 编译两个工程。" >&2
    exit 1
fi

echo "启动自建 Campfire 大厅：$DLL"
echo "默认地址 http://127.0.0.1:5080 —— 只监听本机，符合 ServiceUrl 的 loopback 限制。"
echo "停止：Ctrl+C（房间是内存数据，重启即清空）。"
echo ""
# contentRoot 已在 Program.cs 里钉到程序集目录，因此从任意工作目录启动都能读到 appsettings.json。
exec "$DOTNET" "$DLL" "$@"
