#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增 Linux 侧一键构建（CLI + 自建大厅）+ 协议自检 + 环境自检脚本。

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"

echo "==> 检查 .NET SDK"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "未找到 dotnet，请先安装 .NET 8 SDK：" >&2
  echo "  wget -c https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh" >&2
  echo "  chmod +x /tmp/dotnet-install.sh" >&2
  echo "  /tmp/dotnet-install.sh --channel 8.0 --install-dir \"\$HOME/.dotnet\"" >&2
  echo "  export PATH=\"\$HOME/.dotnet:\$PATH\"" >&2
  exit 1
fi
dotnet --version

echo "==> 构建 Campfire.Cli（完整 Link/ 层 + 编排层 + CLI 会话）"
(cd "$HERE/Campfire.Cli" && dotnet build -c Release)

echo "==> 构建 Campfire.Lobby（ASP.NET Core 自建联机大厅）"
(cd "$HERE/Campfire.Lobby" && dotnet build -c Release)

# 阶段 2 的 GUI 是唯一引入 NuGet 依赖的工程，VM 上必须先由 step5.sh 备好离线包。
# 没有守卫的话，step2.sh 里的 bootstrap 会在这包到位之前就调用本脚本并失败。
echo "==> 构建 Campfire.Avalonia（Avalonia 11 GUI）"
if [ -d "$HERE/offline-nuget" ] || [ -f "$HERE/Campfire.Avalonia/obj/project.assets.json" ]; then
  (cd "$HERE/Campfire.Avalonia" && dotnet build -c Release)
else
  echo "    跳过：离线 NuGet 包尚未就位（虚拟机上先跑 step5.sh）；其余工程照常构建。"
fi

echo "==> 运行协议自检（11 项）"
(cd "$HERE/Campfire.Cli" && dotnet run -c Release --no-build -- selftest)

echo "==> 运行环境自检"
(cd "$HERE/Campfire.Cli" && dotnet run -c Release --no-build -- doctor)

echo ""
echo "构建完成。可选下一步："
echo "  ./run-lobby.sh                 启动自建大厅（默认 http://127.0.0.1:5080）"
echo "  ./verify-lobby.sh              另开终端跑大厅契约验收（42 项）"
echo "  campfire --service-url http://127.0.0.1:5080 lobby list"
