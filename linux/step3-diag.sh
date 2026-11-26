#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「组网失败诊断」脚本。Bazzite 上剪贴板不可用，输出特意压到一屏左右，
#       方便直接截图发回分析；只读，不改系统、不需要 sudo。
#
# 用法（Bazzite 终端，一行）：
#   curl -fsS 172.16.202.127:8000/step3-diag.sh | bash
#
# 检查：easytier-core 能否运行、公共节点 TCP 是否可达、两端 link.log 的错误、
#       加入方控制台日志尾部、是否有残留进程。

set -uo pipefail

ET_CORE="$(find "$HOME/.local/share/campfire-linux" -type f -name 'easytier-core' 2>/dev/null | head -1)"

echo "==== 1/5 easytier-core 能否运行 ===="
if [ -n "$ET_CORE" ] && [ -x "$ET_CORE" ]; then
    echo "  路径: $ET_CORE"
    "$ET_CORE" --version 2>&1 | head -3 || echo "  ✗ --version 执行失败"
else
    echo "  ✗ 找不到 easytier-core（先跑 step2.sh）"
fi

echo ""
echo "==== 2/5 虚拟机内到公共节点的 TCP 11010 ===="
for n in 225284.xyz:11010 easytier.weiai.org.cn:11010 183.230.36.171:11010 et.sumnight.top:11010; do
    h="${n%%:*}"; p="${n##*:}"
    if timeout 3 bash -c "echo > /dev/tcp/$h/$p" 2>/dev/null; then
        echo "  OK   $n"
    else
        echo "  FAIL $n"
    fi
done

echo ""
echo "==== 3/5 两端 Link/link.log 的错误行 ===="
for side in a b; do
    LOG="$HOME/cf-$side/Link/link.log"
    echo "--- cf-$side ---"
    if [ -f "$LOG" ]; then
        grep -inE "error|fail|warn|denied|refused|timeout|超时|失败|拒绝|无法|panic|not found" "$LOG" | tail -10 || echo "  (无匹配错误行)"
        echo "  [最后 6 行]"
        tail -6 "$LOG"
    else
        echo "  ✗ 缺少 $LOG"
    fi
done

echo ""
echo "==== 4/5 加入方控制台日志最后 6 行 ===="
tail -6 "$HOME/cf-b.log" 2>/dev/null || echo "  缺少 ~/cf-b.log"

echo ""
echo "==== 5/5 残留的 easytier 进程 ===="
pgrep -a easytier-core || echo "  无残留（正常，step3.sh 已 stop）"

echo ""
echo "==== 诊断结束：把以上整段截图发我 ===="
