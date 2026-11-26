#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增 Temurin 17 Linux x64 JDK 的断点续传下载脚本（学校网慢/易断的场景）。
#       为什么需要：Bazzite 是不可变系统，不能 dnf install；MC 启动器自带的通常是 JRE，
#       缺 jdk.attach 模块，Java Agent 无法注入游戏去自动开放局域网世界。
#       先在 Windows 侧下好 tarball，放进 serve-for-vm.py 的 HTTP 根目录（本脚本所在目录），
#       虚拟机再走已验证过的局域网通道取，不必挤公网。
#
# 用法（Windows Git Bash）：bash fetch-jdk.sh
# 下好后保持 serve-for-vm.py 窗口开着，虚拟机里执行（IP 以窗口打印的为准）：
#   curl 192.168.1.5:8000/step6.sh | bash
# !! 别把 IP 套进 < > 尖括号，也别用 - 开头的选项。

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ASSET="OpenJDK17U-jdk_x64_linux_hotspot_17.0.21_9.tar.gz"
OUT="$HERE/$ASSET"
GITHUB="https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.21%2B9/${ASSET}"
SOURCES=(
  "https://mirrors.tuna.tsinghua.edu.cn/Adoptium/17/jdk/x64/linux/${ASSET}"
  "$GITHUB"
  "https://ghfast.top/${GITHUB}"
  "https://gh-proxy.com/${GITHUB}"
)

echo "资产：${ASSET}（Temurin 17.0.21+9，Linux x64，约 185 MB）"
echo "输出：${OUT}"
echo

if ! command -v curl >/dev/null 2>&1; then
    echo "需要 curl。也可浏览器打开清华镜像目录手动下载后放到本目录。" >&2
    exit 1
fi

# 已下载且完整就跳过，方便反复运行。
if [ -f "$OUT" ] && gzip -t "$OUT" >/dev/null 2>&1; then
    echo "已存在且 gzip 校验通过，跳过下载：${OUT}"
else
    for URL in "${SOURCES[@]}"; do
        echo "==> 尝试 ${URL}"
        # -C - 断点续传；失败自动重试；--connect-timeout 避免卡死在不可达的源。
        if curl -L -C - --retry 5 --retry-delay 2 --connect-timeout 15 \
                --fail --output "$OUT" "$URL"; then
            if gzip -t "$OUT" >/dev/null 2>&1; then
                echo "下载完成且压缩包完整。"
                break
            fi
            echo "下载到的文件不是有效 gzip，尝试下一个源…" >&2
        fi
    done
    if ! gzip -t "$OUT" >/dev/null 2>&1; then
        echo "所有下载源都失败了。可手动下载后放到：${OUT}" >&2
        exit 1
    fi
fi

# 清华镜像不提供 .sha256.txt，所以在本地按实际字节重算，供虚拟机端校验。
if command -v sha256sum >/dev/null 2>&1; then
    (cd "$HERE" && sha256sum "$ASSET" > "$ASSET.sha256")
    echo "校验文件：${OUT}.sha256"
    cat "$OUT.sha256"
fi

ls -lh "$OUT"
echo
echo "下一步（保持 serve-for-vm.py 窗口开着，虚拟机里手敲一行）："
echo "  curl 192.168.1.5:8000/step6.sh | bash"
echo "  （IP 换成服务窗口打印的；别套尖括号）"
