#!/usr/bin/env bash
# Campfire Linux port — 新增（非改写）：把 pack-release.sh 组装好的 stage 目录
# 或已发布的 tar.gz 变成单文件 AppImage。必须在 Linux 上运行（GitHub Actions
# ubuntu-latest 或 Bazzite VM 均可），因为 appimagetool 需要 squashfs/FUSE。
#
#   bash build-appimage.sh <stage-dir-or-tar.gz> <output.AppImage>
#
set -euo pipefail

SRC="${1:?usage: build-appimage.sh <stage-dir-or-tar.gz> <output.AppImage>}"
OUT="${2:?usage: build-appimage.sh <stage-dir-or-tar.gz> <output.AppImage>}"
VERSION="${VERSION:-1.1.32}"
APP_ID="${APP_ID:-io.github.campfire.Campfire}"
REL="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ---- 1. 取得组装好的 stage ----
if [ -d "$SRC" ]; then
    STAGE="$(cd "$SRC" && pwd)"
elif [ -f "$SRC" ]; then
    tar -xzf "$SRC" -C "$WORK"
    STAGE="$(find "$WORK" -mindepth 1 -maxdepth 1 -type d | head -1)"
else
    echo "✗ 输入不存在：$SRC" >&2
    exit 1
fi
[ -f "$STAGE/payload/Campfire.Avalonia" ] || { echo "✗ stage 里没有主程序" >&2; exit 1; }

# ---- 2. 组装 AppDir ----
DIR="$WORK/AppDir"
mkdir -p "$DIR/usr/bin" "$DIR/usr/lib/campfire" \
         "$DIR/usr/share/applications" "$DIR/usr/share/metainfo" \
         "$DIR/usr/share/icons/hicolor/256x256/apps" "$DIR/usr/share/doc/campfire"

cp -a "$STAGE/payload/." "$DIR/usr/bin/"
chmod 0755 "$DIR/usr/bin/Campfire.Avalonia"
find "$DIR/usr/bin" -name '*.so' -exec chmod 0755 {} \;

# EasyTier zip 放这里，AppRun 会设 CAMPFIRE_EASYTIER_ZIP 实现离线首次联机。
for zip in "$STAGE/payload"/easytier-linux-*.zip; do
    [ -f "$zip" ] && cp "$zip" "$DIR/usr/lib/campfire/" && break
done

cp "$REL/AppRun" "$DIR/AppRun"
chmod 0755 "$DIR/AppRun"

cp "$STAGE/assets/campfire.png" "$DIR/usr/share/icons/hicolor/256x256/apps/$APP_ID.png"
cp "$STAGE/assets/campfire.png" "$DIR/$APP_ID.png"
cp "$STAGE/assets/campfire.png" "$DIR/.DirIcon"

sed -e "s|@EXEC@|campfire|g" -e "s|@ICON@|$APP_ID|g" \
    "$STAGE/assets/campfire.desktop.in" > "$DIR/usr/share/applications/$APP_ID.desktop"
cp "$DIR/usr/share/applications/$APP_ID.desktop" "$DIR/$APP_ID.desktop"

[ -f "$STAGE/assets/campfire.metainfo.xml" ] && \
    cp "$STAGE/assets/campfire.metainfo.xml" "$DIR/usr/share/metainfo/$APP_ID.metainfo.xml"
for f in LICENSE THIRD-PARTY-NOTICES.md; do
    [ -f "$STAGE/docs/$f" ] && cp "$STAGE/docs/$f" "$DIR/usr/share/doc/campfire/"
done

# ---- 3. 找 appimagetool（优先用调用方预置的，避免 CI 联网） ----
TOOL="${APPIMAGETOOL:-}"
if [ -z "$TOOL" ] && command -v appimagetool >/dev/null 2>&1; then
    TOOL="$(command -v appimagetool)"
fi
if [ -z "$TOOL" ]; then
    echo "==> 下载 appimagetool"
    curl -fL --max-time 180 -o "$WORK/appimagetool.AppImage" \
        "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$WORK/appimagetool.AppImage"
    # CI 上没有 FUSE，用自解压方式运行 appimagetool 本体。
    (cd "$WORK" && ./appimagetool.AppImage --appimage-extract >/dev/null 2>&1)
    TOOL="$WORK/squashfs-root/AppRun"
fi

# ---- 4. 打包 ----
echo "==> appimagetool -> $OUT"
ARCH=x86_64 "$TOOL" --no-appstream "$DIR" "$OUT" 2>&1 | tail -20
chmod 0755 "$OUT"

"$OUT" --appimage-version >/dev/null 2>&1 || true
echo "  产物: $OUT ($(du -h "$OUT" | cut -f1))"
echo "  提示: 用户下载后 chmod +x 即可双击运行；无需 root、不改动系统。"
