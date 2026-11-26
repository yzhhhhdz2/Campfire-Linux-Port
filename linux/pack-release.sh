#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 新增：面向最终用户的一键分发打包。在 Windows (Git Bash) 或 Linux 上都能跑，
# 用 .NET 8 SDK 交叉编译出 linux-x64 自包含程序，组装成带 install.sh 的 tar.gz。
#
#   bash pack-release.sh                    # 默认内置 EasyTier，产物在 dist/
#   INCLUDE_EASYTIER=0 bash pack-release.sh # 更小的包，首次联机时自动下载组件
#
# 合规硬约束：绝不包含 Minecraft 客户端 / Mojang assets / Temurin JDK。
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
REL="$HERE/release"

VERSION="${VERSION:-1.1.32}"
APP_ID="${APP_ID:-io.github.campfire.Campfire}"
INCLUDE_EASYTIER="${INCLUDE_EASYTIER:-1}"
RELEASE_DATE="${RELEASE_DATE:-$(date -u +%F)}"
OUT_DIR="${OUT_DIR:-$HERE/dist}"
# 每次用独立的临时目录：避免清理上一次的 200+ 文件（批量删除保护），
# 也让多次构建互不干扰。产物只写 OUT_DIR，stage 供排查用、不自动删。
STAGE="$(mktemp -d)/campfire-$VERSION"
OUT="$OUT_DIR/campfire-$VERSION-linux-x64.tar.gz"

# ---- 0. 工具探测（Windows 交叉编译 / Linux CI 都适用） ----
# 注意：Windows PATH 上的 C:\Program Files\dotnet\dotnet 常常只有运行时、没有 SDK
# （`dotnet publish` 会报 "No .NET SDKs were found"）。项目约定的 SDK 在用户目录，
# 必须优先使用；Linux CI 上则走 setup-dotnet 装进 PATH 的那个。
if [ -x "/c/Users/yzhhh/.dotnet/dotnet.exe" ]; then
    DOTNET="/c/Users/yzhhh/.dotnet/dotnet.exe"
elif [ -n "${DOTNET_SDK:-}" ] && [ -x "$DOTNET_SDK" ]; then
    DOTNET="$DOTNET_SDK"
elif command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
    DOTNET=dotnet
else
    echo "✗ 找不到 .NET 8 SDK。设置 DOTNET_SDK=/path/to/dotnet 后重试。" >&2
    exit 1
fi
if command -v python3 >/dev/null 2>&1; then PY=python3
elif command -v python >/dev/null 2>&1; then PY=python
else echo "✗ 找不到 python。" >&2; exit 1; fi

# homepage 优先用 git remote，其次环境变量，最后退回上游仓库。
if [ -z "${REPO_URL:-}" ]; then
    REPO_URL="$(cd "$ROOT" && git remote get-url origin 2>/dev/null || true)"
    REPO_URL="${REPO_URL%.git}"
    REPO_URL="${REPO_URL#git@github.com:}"
    case "$REPO_URL" in
        git@github.com:*|*:*) REPO_URL="https://github.com/$REPO_URL" ;;
    esac
    [ -n "$REPO_URL" ] || REPO_URL="https://github.com/Xocio/Campfire"
fi
[ -n "${REPO_URL:-}" ] || REPO_URL="https://github.com/Xocio/Campfire"

echo "==> 版本 $VERSION / app-id $APP_ID / 日期 $RELEASE_DATE"
echo "    homepage $REPO_URL"
echo "    dotnet   $DOTNET"

# ---- 1. 交叉编译 linux-x64 自包含程序 ----
mkdir -p "$STAGE/payload" "$STAGE/assets" "$STAGE/docs"
echo "==> dotnet publish -r linux-x64 --self-contained true"
"$DOTNET" publish "$HERE/Campfire.Avalonia/Campfire.Avalonia.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -o "$STAGE/payload" -v minimal

# ---- 2. 验证产物确实是 Linux ELF（防止误打进 Windows 的 PE） ----
"$PY" - "$STAGE/payload/Campfire.Avalonia" <<'PY'
import sys
d = open(sys.argv[1], 'rb').read(20)
if d[:4] != b'\x7fELF':
    sys.exit('✗ 主程序不是 ELF，拒绝打包')
print('  ELF 校验 OK: x86-64=%s' % (d[18:20] == b'\x3e\x00'))
PY

# ---- 3. 组装发布素材 ----
cp "$REL/assets/campfire.desktop.in" "$STAGE/assets/"
cp "$REL/assets/campfire.png" "$STAGE/assets/"
sed -e "s|@APP_ID@|$APP_ID|g" \
    -e "s|@VERSION@|$VERSION|g" \
    -e "s|@DATE@|$RELEASE_DATE|g" \
    -e "s|@REPO_URL@|$REPO_URL|g" \
    "$REL/assets/campfire.metainfo.xml.in" > "$STAGE/assets/campfire.metainfo.xml"
cp "$REL/AppRun" "$REL/install.sh" "$REL/uninstall.sh" "$STAGE/"
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$STAGE/docs/"
# 内置 EasyTier（LGPL-3.0）时随包附许可证全文；CI 会先下载好。
if [ -f "$REL/assets/LGPL-3.0.txt" ]; then
    cp "$REL/assets/LGPL-3.0.txt" "$STAGE/docs/"
fi

# 三语 README 的「安装」入口，离线可读。
for f in README.md README.zh-Hant.md README.en.md; do
    [ -f "$HERE/$f" ] && cp "$HERE/$f" "$STAGE/docs/"
done

cat > "$STAGE/docs/SOURCE.txt" <<EOF
Campfire $VERSION — Corresponding source (GPL-3.0-or-later)
============================================================

This archive contains a self-contained BINARY build. The complete
corresponding source code for it is distributed alongside this release as:

    campfire-linux.tar.gz

and is also available from the repository at the matching tag:

    $REPO_URL/tree/v$VERSION

The source archive includes everything needed to rebuild this binary:
  - linux/                all C# sources, XAML, styles, localization, themes
  - linux/release/         launcher, .desktop, AppStream metadata, packaging scripts
  - src/Campfire/Hex.cs    and src/Campfire/Link/**  (linked, unmodified protocol layer)
  - src/Campfire/Link/Minecraft/Mappings/*.txt      (embedded legacy name tables)
  - src/java-agent/        full Java sources, build script and the three jars
                           that are embedded into Campfire.Avalonia.dll
  - LICENSE, THIRD-PARTY-NOTICES.md

Third-party components: see docs/THIRD-PARTY-NOTICES.md and, when bundled,
docs/LGPL-3.0.txt (EasyTier 2.6.4, unmodified, LGPL-3.0).
Minecraft client files and Mojang assets are NOT distributed here.
EOF

cat > "$STAGE/INSTALL.txt" <<EOF
Campfire $VERSION — Linux x86-64 self-contained build
=====================================================

Install (no root required, works on immutable systems like Bazzite):

    tar -xzf campfire-$VERSION-linux-x64.tar.gz
    cd campfire-$VERSION-linux-x64
    bash install.sh

Then launch from your application menu, or run:

    \$HOME/.local/bin/campfire

Uninstall:

    bash uninstall.sh

No system .NET installation is required. Everything lands under
\$HOME/.local; settings and logs live in \$HOME/.local/share/campfire.

Minecraft and a JDK are NOT included and are never redistributed here.
See docs/THIRD-PARTY-NOTICES.md for licensing.
EOF

# ---- 4. 可选内置 EasyTier（LGPL-3.0，未修改的独立程序） ----
if [ "$INCLUDE_EASYTIER" = "1" ]; then
    ZIP="$(ls "$HERE"/easytier-linux-x86_64-v*.zip 2>/dev/null | head -1 || true)"
    if [ -n "$ZIP" ]; then
        cp "$ZIP" "$STAGE/payload/"
        echo "  内置 EasyTier: $(basename "$ZIP")（首次联机可完全离线）"
    else
        echo "  ⚠ 未找到 EasyTier zip，改为按需下载模式。"
    fi
else
    echo "  跳过 EasyTier（INCLUDE_EASYTIER=0），首次联机会自动下载。"
fi

# ---- 5. 打包（Python 精确设置 Unix 权限位 + LF 校验和） ----
echo "==> 生成 tar.gz"
"$PY" "$REL/make_tarball.py" "$STAGE" "$OUT" "$VERSION"

echo "==> 校验"
(cd "$OUT_DIR" && sha256sum -c "$(basename "$OUT").sha256")
echo "  包内大二进制/禁用内容核对："
if tar -tzf "$OUT" | grep -qiE 'mc-26\.3-offline|OpenJDK|\.nupkg|client\.jar|assets/objects'; then
    echo "  ✗ 检测到禁止分发的内容！" >&2
    exit 1
else
    echo "    OK：无 MC 客户端 / assets / JDK / nupkg"
fi

echo "  组装目录（临时，可手动删除）: $STAGE"
echo ""
echo "==== 完成 ===="
echo "  $OUT"
echo "  $OUT.sha256"
echo ""
echo "上传到 GitHub Release 后，用户从 Release 页面下载下面这个文件即可："
echo "  campfire-$VERSION-linux-x64.tar.gz"
echo "  然后解压并运行 bash install.sh（完整步骤见包内 INSTALL.txt）。"
