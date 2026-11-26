#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 新增：用户级卸载。只删安装时写入的文件；数据目录（设置/日志/EasyTier）保留。
set -euo pipefail

HERE="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
PREFIX="${PREFIX:-${XDG_DATA_HOME:-$HOME/.local}}"
APP_ID="${APP_ID:-io.github.campfire.Campfire}"
DATA="${CAMPFIRE_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/campfire}"

rm -rf "$PREFIX/lib/campfire"
rm -f "$PREFIX/bin/campfire"
rm -f "$PREFIX/share/applications/$APP_ID.desktop"
rm -f "$PREFIX/share/icons/hicolor/256x256/apps/$APP_ID.png"
rm -f "$PREFIX/share/metainfo/$APP_ID.metainfo.xml"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
fi

echo "==== Campfire 已卸载 ===="
echo "  以下数据目录按设计保留（含设置、日志与 EasyTier 组件）："
echo "    $DATA"
echo "  如确认不再需要，可手动删除： rm -rf \"\$HOME/.local/share/campfire\""
