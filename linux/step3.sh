#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「一键回环自检」脚本。VMware 剪贴板共享在 Bazzite 上不可用，
#       所以本脚本自己起 host + join 两个会话，从各自的 session.json 里读邀请码与阶段，
#       全程不需要复制粘贴、不需要开两个终端、不需要手抄邀请码。
#
# 用法（Bazzite 终端，一行）：
#   curl -fsS 172.16.202.127:8000/step3.sh | bash
#
# 产出：~/cf-code.txt（邀请码）、~/cf-a.log、~/cf-b.log（两端完整日志）
# 只写 $HOME，不改系统层、不需要 sudo；结束时自动停掉两个会话并释放房间。
# 注意：--skip-probe 只用于本自检（没有真实 MC 世界），正式联机不要加。

set -euo pipefail

ROOT="$HOME/campfire-linux"
CLI_DLL="$ROOT/linux/Campfire.Cli/bin/Release/net8.0/campfire.dll"
A="$HOME/cf-a"
B="$HOME/cf-b"
PORT="${CF_PORT:-43210}"

if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if [ ! -f "$CLI_DLL" ] || ! command -v dotnet >/dev/null 2>&1; then
    echo "✗ 环境没准备好，先跑第②步： curl -fsS <主机IP>:8000/step2.sh | bash" >&2
    exit 1
fi
if ! dotnet "$CLI_DLL" easytier check >/dev/null 2>&1; then
    echo "✗ EasyTier 组件没装好，先跑第②步。" >&2
    exit 1
fi

cf() { dotnet "$CLI_DLL" "$@"; }

echo "==== Campfire 第③步：回环联机自检（免剪贴板）===="
echo "  房主端口 : $PORT    玩家 : HostA / GuestB"
echo "  数据目录 : $A , $B"

mkdir -p "$A" "$B"
: > "$HOME/cf-a.log"
: > "$HOME/cf-b.log"
: > "$HOME/cf-code.txt"

HOST_PID=""
JOIN_PID=""
cleanup() {
    cf --data-dir "$A" stop >/dev/null 2>&1 || true
    cf --data-dir "$B" stop >/dev/null 2>&1 || true
    sleep 2
    if [ -n "$HOST_PID" ]; then kill "$HOST_PID" 2>/dev/null || true; fi
    if [ -n "$JOIN_PID" ]; then kill "$JOIN_PID" 2>/dev/null || true; fi
}
trap cleanup EXIT INT TERM

# ---- 1. 起房主，等它把邀请码写进 session.json ----
echo "  启动房主（后台）…"
cf --data-dir "$A" host --port "$PORT" --player HostA --skip-probe > "$HOME/cf-a.log" 2>&1 &
HOST_PID=$!

CODE=""
for i in $(seq 1 60); do
    if [ -f "$A/session.json" ]; then
        CODE="$(sed -n 's/^[[:space:]]*"Code":[[:space:]]*"\([^"]*\)".*/\1/p' "$A/session.json" | head -1)"
    fi
    if [ -n "$CODE" ]; then break; fi
    if grep -q "联机失败" "$HOME/cf-a.log" 2>/dev/null; then break; fi
    if ! kill -0 "$HOST_PID" 2>/dev/null; then break; fi
    if [ $((i % 10)) -eq 0 ]; then echo "  ...等房主生成邀请码 ${i}s"; fi
    sleep 1
done

if [ -z "$CODE" ]; then
    echo "✗ 房主没能生成邀请码，日志如下：" >&2
    tail -40 "$HOME/cf-a.log" >&2
    exit 1
fi
echo "  邀请码   : $CODE   （已写入 ~/cf-code.txt）"
echo "$CODE" > "$HOME/cf-code.txt"

# ---- 2. 用这个码起加入方，等握手到 Ready / Failed ----
echo "  启动加入方（后台）…"
cf --data-dir "$B" join --code "$CODE" --player GuestB --skip-probe > "$HOME/cf-b.log" 2>&1 &
JOIN_PID=$!

STAGE=""
READY_BANNER=no
for i in $(seq 1 90); do
    if [ -f "$B/session.json" ]; then
        STAGE="$(sed -n 's/^[[:space:]]*"Stage":[[:space:]]*"\([^"]*\)".*/\1/p' "$B/session.json" | head -1)"
    fi
    if [ "$STAGE" = "Ready" ] || [ "$STAGE" = "Failed" ]; then break; fi
    # 兜底：控制台的「已加入房间」横幅只有在 LinkStage.Ready 时才会打印
    # （CliHost.PrintReadyBanner）。session.json 每 2 秒才写一次，慢一拍也不能误判成失败。
    if grep -q "已加入房间" "$HOME/cf-b.log" 2>/dev/null; then READY_BANNER=yes; break; fi
    if grep -q "联机失败" "$HOME/cf-b.log" 2>/dev/null; then break; fi
    if ! kill -0 "$JOIN_PID" 2>/dev/null; then break; fi
    if [ $((i % 10)) -eq 0 ]; then echo "  ...等加入方握手 ${i}s (阶段: ${STAGE:-未开始})"; fi
    sleep 1
done

# ---- 3. 打印两端结果（必须在 stop 之前，stop 会清掉 session.json）----
echo ""
echo "==== A（房主） status ===="
cf --data-dir "$A" status || true
echo ""
echo "==== B（加入方） status ===="
cf --data-dir "$B" status || true
echo ""
echo "==== B peers ===="
cf --data-dir "$B" peers || true
echo ""
echo "==== A 日志（最后 25 行）===="
tail -25 "$HOME/cf-a.log"
echo ""
echo "==== B 日志（最后 25 行）===="
tail -25 "$HOME/cf-b.log"
echo ""
# ---- 4. 结论：必须在 EXIT trap 触发前读快照，stop 会删掉 session.json ----
read_field() {
    sed -n "s/^[[:space:]]*\"$2\":[[:space:]]*\"\?\([^\",]*\)\"\?.*/\1/p" "$1" 2>/dev/null | head -1 || true
}
A_STAGE="$(read_field "$A/session.json" Stage)"
B_STAGE="$(read_field "$B/session.json" Stage)"
B_PLAYERS="$(read_field "$B/session.json" PlayerCount)"
FOUND_HOST=no
if grep -q "Found host" "$B/Link/link.log" 2>/dev/null; then FOUND_HOST=yes; fi
HAS_BANNER=no
if grep -q "已加入房间" "$HOME/cf-b.log" 2>/dev/null; then HAS_BANNER=yes; fi

echo ""
echo "==== 第③步结论 ===="
if [ "$B_STAGE" = "Ready" ] || [ "$HAS_BANNER" = "yes" ] || [ "$READY_BANNER" = "yes" ]; then
    echo "  ✅ 通过：房主建房 → 邀请码 → EasyTier 组网 → 加入方 Ready / 已加入房间"
    echo "     A 阶段=$A_STAGE  B 阶段=${B_STAGE:-Ready(banner)}  B 玩家=${B_PLAYERS:-未知}  FoundHost=$FOUND_HOST"
    echo "     下一步：阶段 1 硬标准 ④ Linux ⇄ 官方 Windows 客户端双向用邀请码互进。"
elif [ "$B_STAGE" = "Failed" ]; then
    echo "  ✗ 失败：加入方 LinkStage.Failed，看上面 B 日志与 Link/link.log。"
else
    echo "  ✗ 超时：90 秒内没到 Ready。看上面 status/peers 与两段日志，别急着改代码。"
fi
echo "  邀请码：$CODE  (~/cf-code.txt)"
echo "  完整日志：~/cf-a.log、~/cf-b.log、~/cf-b/Link/link.log；两个会话已自动停止。"
