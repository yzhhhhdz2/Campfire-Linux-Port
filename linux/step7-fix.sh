#!/usr/bin/env bash
# Campfire Linux port — 基于 Xocio/Campfire v1.1.32 (GPL-3.0)，新增文件（非改写）。
# 作用：修复第⑦步离线包里 classpath.txt 的两个 Windows 坑。
#
# 症状：bash ~/minecraft-26.3.sh 报
#   错误: 找不到或无法加载主类 net.minecraft.client.main.Main
#   原因: java.lang.ClassNotFoundException: net.minecraft.client.main.Main
# 根因：classpath.txt 在 Windows 上生成时，pathlib.Path 把分隔符写成了反斜杠（Linux 不认），
#       write_text 又把行尾写成 CRLF（最后一个 jar 路径尾巴带 CR）。两者叠加导致 66 个库
#       和 client.jar 全部没进有效 classpath。与 Java 版本无关（不是 UnsupportedClassVersionError）。
#
# 本脚本只重建 classpath.txt（约 4 KB），**不用重下 497 MB 游戏包**。
# 用法（Bazzite 终端，一行；IP 以服务窗口打印的为准）：
#   curl 192.168.1.5:8000/step7-fix.sh | bash
# !! 别把 IP 套进尖括号，也别用以横杠开头的选项组合。

set -euo pipefail

MC="$HOME/.local/campfire-mc263/mc-26.3"

echo "==== 修复 Minecraft 26.3 的 classpath.txt ===="

if [ ! -d "$MC/libraries" ] || [ ! -f "$MC/versions/26.3/26.3.jar" ]; then
    echo "✗ 游戏目录不完整：$MC" >&2
    echo "  先跑第⑦步 step7.sh 把游戏包解开。" >&2
    exit 1
fi

cd "$MC"
# find 在 Linux 上天然输出正斜杠相对路径；sort 后拼成 ':'- 分隔的一行，最后补上 client.jar。
CP="$(find libraries -type f -name '*.jar' | sort | tr '\n' ':')versions/26.3/26.3.jar"
printf '%s\n' "$CP" > "$MC/classpath.txt"

# ---- 自检：不能有反斜杠、不能有 CR、条目数要对 ----
if grep -qF '\' "$MC/classpath.txt"; then
    echo "✗ classpath.txt 里还有反斜杠。" >&2
    exit 1
fi
if grep -q $'\r' "$MC/classpath.txt"; then
    echo "✗ classpath.txt 里还有 CR。" >&2
    exit 1
fi
COUNT="$(tr ':' '\n' < "$MC/classpath.txt" | grep -c '\.jar')"
if [ "$COUNT" -lt 60 ]; then
    echo "✗ 只找到 $COUNT 个 jar，少于预期的 67 个。" >&2
    exit 1
fi
if ! tail -c 30 "$MC/classpath.txt" | grep -q "versions/26.3/26.3.jar"; then
    echo "✗ client.jar 没在 classpath 末尾。" >&2
    exit 1
fi

echo "  已重建 : $MC/classpath.txt"
echo "  jar 数 : $COUNT（正斜杠、LF、无 CR）"
echo "  开头   : $(cut -c1-72 "$MC/classpath.txt")"
echo "  结尾   : $(tail -c 32 "$MC/classpath.txt")"
echo ""
echo "==== 修好了，现在重新跑终端 A ===="
echo "  bash \$HOME/minecraft-26.3.sh"
echo ""
echo "这次应该能进主菜单（Java 25 与 26.3 客户端本身没问题）。"
echo "如果换成别的报错："
echo "  认证相关 → MC_USER_TYPE=legacy bash \$HOME/minecraft-26.3.sh"
echo "  SDL3 窗口起不来 → MC_SDL_DRIVER=x11 bash \$HOME/minecraft-26.3.sh"
echo "  仍找不到主类 → 把 classpath.txt 前 80 个字符发我。"
