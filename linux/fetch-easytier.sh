#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增 EasyTier 2.6.4 Linux 组件的断点续传下载脚本（学校网慢/易断的场景）。
#
# 用法：bash fetch-easytier.sh [输出文件.zip]
# 下好后：campfire easytier install --local <输出文件.zip>

set -euo pipefail

VERSION="2.6.4"   # 必须与官方 Windows 客户端一致，禁止改动

case "$(uname -m)" in
  x86_64|amd64)   ET_ARCH="x86_64" ;;
  aarch64|arm64)  ET_ARCH="aarch64" ;;
  armv7l|armhf)   ET_ARCH="armv7hf" ;;
  *) echo "不支持的架构：$(uname -m)。可用资产名见 GitHub Release v${VERSION}。" >&2; exit 1 ;;
esac

ASSET="easytier-linux-${ET_ARCH}-v${VERSION}.zip"
OUT="${1:-$PWD/$ASSET}"
GITHUB="https://github.com/EasyTier/EasyTier/releases/download/v${VERSION}/${ASSET}"
SOURCES=(
  "https://gitee.com/easytier/EasyTier/releases/download/v${VERSION}/${ASSET}"
  "$GITHUB"
  "https://ghfast.top/${GITHUB}"
  "https://gh-proxy.com/${GITHUB}"
)

echo "架构：$(uname -m) → ${ET_ARCH}"
echo "资产：${ASSET}"
echo "输出：${OUT}"
echo

if ! command -v curl >/dev/null 2>&1; then
  echo "需要 curl。也可用 wget -c "${GITHUB}" -O "${OUT}"，或 aria2c -c -x 8 "${GITHUB}"。" >&2
  exit 1
fi

for URL in "${SOURCES[@]}"; do
  echo "==> 尝试 ${URL}"
  # -C - 断点续传；失败自动重试；--connect-timeout 避免卡死在不可达的源。
  if curl -L -C - --retry 5 --retry-delay 2 --connect-timeout 15 \
       --fail --output "$OUT" "$URL"; then
    if command -v unzip >/dev/null 2>&1 && unzip -t "$OUT" >/dev/null 2>&1; then
      echo "压缩包完整：${OUT}"
      echo
      echo "下一步（在 Linux 上）："
      echo "  campfire easytier install --local ${OUT}"
      echo "  campfire easytier check"
      exit 0
    elif ! command -v unzip >/dev/null 2>&1; then
      echo "已下载 ${OUT}（本机没有 unzip，跳过完整性检查）"
      echo "  campfire easytier install --local ${OUT}"
      exit 0
    fi
    echo "下载到的文件不是有效 zip，尝试下一个源…" >&2
  fi
done

echo "所有下载源都失败了。可手动下载后放到本地再指定路径：" >&2
echo "  ${GITHUB}" >&2
echo "  campfire easytier install --local /path/to/${ASSET}" >&2
exit 1
