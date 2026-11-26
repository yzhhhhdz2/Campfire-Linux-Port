#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 作用：在 Windows(Git Bash) 或 Linux 上生成一个**自包含的源码包**，可直接搬到 Ubuntu 解压编译。
#
# 用法：bash pack-for-linux.sh [输出文件.tar.gz]
# 默认输出：与脚本同目录的 campfire-linux.tar.gz
#
# 为什么必须打包 src/Campfire/：两个 .csproj 用 $(MSBuildThisFileDirectory)../../src/Campfire/
# 共享源码链接复用原协议层（防协议漂移）。只拷 linux/ 目录过去，Linux 上必然报「找不到源文件」。

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
OUT="${1:-$HERE/campfire-linux.tar.gz}"
# 临时 staging 放在系统 /tmp，由系统自行清理；这里不做递归删除，
# 因为部分环境的安全策略会拦截 rm -rf 并把脚本误判为失败。
STAGE="$(mktemp -d)"
PKG="$STAGE/campfire-linux"

# 包内根目录固定为 campfire-linux/，解压后路径不含空格与中文（Linux 上最省心）。
mkdir -p "$PKG/src/Campfire" "$PKG/linux"

echo "==> 1/6 GPL-3.0 合规文件与原项目说明"
for f in LICENSE THIRD-PARTY-NOTICES.md README.md NETWORK.md; do
    if [ -f "$ROOT/$f" ]; then cp "$ROOT/$f" "$PKG/$f"; else echo "  警告：缺少 $f" >&2; fi
done

echo "==> 2/6 被链接的原协议层（Hex.cs + Link/**/*.cs，逐字未改）"
if [ ! -f "$ROOT/src/Campfire/Hex.cs" ]; then
    echo "找不到 $ROOT/src/Campfire/Hex.cs —— 请在完整的源码根目录下运行本脚本。" >&2
    exit 1
fi
cp "$ROOT/src/Campfire/Hex.cs" "$PKG/src/Campfire/Hex.cs"
# Mappings/*.txt 是两个 csproj 的 EmbeddedResource（老版本混淆名对照表），也必须进包。
(cd "$ROOT/src/Campfire" && find Link -type f \( -name '*.cs' -o -path '*/Mappings/*' -name '*.txt' \) -print) | while IFS= read -r f; do
    mkdir -p "$PKG/src/Campfire/$(dirname "$f")"
    cp "$ROOT/src/Campfire/$f" "$PKG/src/Campfire/$f"
done
echo "    已收 $(find "$PKG/src/Campfire" -type f -name '*.cs' | wc -l) 个原协议层文件、$(find "$PKG/src/Campfire/Link/Minecraft/Mappings" -type f -name '*.txt' 2>/dev/null | wc -l) 个老版本名字表"

# Java Agent：两个 csproj 用 EmbeddedResource 引用 src/java-agent/dist/*.jar。
# GPL-3.0 要求分发二进制时提供完整对应源码，所以把 Java 源码、jar、构建脚本一起收进包。
if [ -d "$ROOT/src/java-agent" ]; then
    mkdir -p "$PKG/src/java-agent"
    (cd "$ROOT/src/java-agent" && find . -type f -print) | while IFS= read -r f; do
        mkdir -p "$PKG/src/java-agent/$(dirname "$f")"
        cp "$ROOT/src/java-agent/$f" "$PKG/src/java-agent/$f"
    done
    echo "    已收 $(find "$PKG/src/java-agent" -type f | wc -l) 个 Java Agent 文件（含 3 个 jar）"
else
    echo "  警告：缺少 src/java-agent，自动开放局域网功能将不可用" >&2
fi

echo "==> 3/6 本分支新增的 linux/（排除 bin/obj/.testdata、包自身与按需下载的第三方二进制）"
# EasyTier 是 LGPL-3.0 独立组件，走 fetch-easytier.sh 按需下载，不进源码包；
# 预下载到本目录只是为了方便用 serve-for-vm.py 传给虚拟机。
(cd "$HERE" && find . -type f \
    -not -path '*/bin/*' -not -path '*/obj/*' \
    -not -path '*/__pycache__/*' -not -name '*.pyc' \
    -not -path './.testdata/*' \
    -not -path './dist/*' \
    -not -name 'campfire-linux.tar.gz' -not -name 'campfire-linux.tar.gz.sha256' \
    -not -name 'mc-26.3-offline.tar.gz' -not -name 'mc-26.3-offline.tar.gz.sha256' \
    -not -name 'easytier-linux-*.zip' -not -name 'easytier-linux-*.zip.sha256' \
    -not -name 'OpenJDK*-jdk_x64_linux_*.tar.gz' -not -name 'OpenJDK*-jdk_x64_linux_*.tar.gz.sha256' \
    -not -path './offline-nuget/*' \
    -not -name 'offline-nuget.tar.gz' -not -name 'offline-nuget.tar.gz.sha256' \
    -not -name '*.nupkg' \
    -print) | while IFS= read -r f; do
    mkdir -p "$PKG/linux/$(dirname "$f")"
    cp "$HERE/$f" "$PKG/linux/$f"
done

echo "==> 4/6 bootstrap 放到包根，Linux 上一条命令即可初始化"
cp "$HERE/bootstrap.sh" "$PKG/bootstrap.sh"

echo "==> 5/6 统一 LF 换行 + 可执行位（CRLF 的 .sh 在 Linux 会报 bad interpreter）"
# 不用 xargs：Git Bash 的 xargs 在文件较多时会命中 arg_max 断言（xargs.c:512）直接 abort。
# find -exec 逐个调用，十几个脚本没有性能问题，Linux / macOS / Git Bash 都一致。
find "$PKG" -type f -name '*.sh' -exec sed -i 's/\r$//' {} \;
chmod +x "$PKG/bootstrap.sh" "$PKG/linux"/*.sh

echo "==> 6/6 打包"
mkdir -p "$(dirname "$OUT")"
(cd "$STAGE" && tar -czf "$OUT" campfire-linux)
if command -v sha256sum >/dev/null 2>&1; then
    (cd "$(dirname "$OUT")" && sha256sum "$(basename "$OUT")" > "$(basename "$OUT").sha256")
fi

echo ""
echo "完成：$OUT"
ls -lh "$OUT"
echo "包内文件数：$(find "$PKG" -type f | wc -l)"
echo ""
echo "包结构（前 12 项）："
# 先落盘再取前几行：直接 `tar -tzf | head` 会让 tar 收到 SIGPIPE，
# 在 set -o pipefail 下把脚本退出码变成 141（明明打包成功了却像失败）。
tar -tzf "$OUT" > "$STAGE/listing.txt"
sed -n '1,12p' "$STAGE/listing.txt"
echo ""
echo "搬到 Linux 后：tar -xzf campfire-linux.tar.gz && cd campfire-linux && bash bootstrap.sh"
