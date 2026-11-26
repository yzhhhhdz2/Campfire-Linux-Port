#!/usr/bin/env bash
# Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
# 修改：新增「与官方 Windows 客户端双向互通」脚本。VMware 剪贴板在 Bazzite 上不可用，
#       所以邀请码靠屏幕显示 + 手敲一次，其余全部由脚本完成；交互输入一律读 /dev/tty，
#       避免被 curl | bash 的管道吃掉。
#       v2：host/join 改为后台运行 + 日志落盘 + 状态监控。CLI 会话一旦 Stage=Failed
#       会自行退出，前台跑的话窗口一闪就没线索；现在退出时会打印退出原因与日志尾部。
#
# 用法（Bazzite 在家目录 ~ 下执行，两行；不带任何 - 选项，避免手敲漏横杠）：
#   curl 172.16.202.127:8000/step4.sh > step4.sh
#   bash step4.sh
#
# 阶段 1 硬标准：两个方向都要通
#   方向 A：本脚本起 Linux 房 → Windows 官方 Campfire.exe 输入邀请码加入
#   方向 B：Windows 官方客户端建房 → 本脚本选 3 输入它的邀请码加入
# 房间层互通用 --skip-probe（虚拟机没装 MC 也能测）；正式进 MC 时选 2，且不要加它。

set -uo pipefail

ROOT="$HOME/campfire-linux"
CLI_DLL="$ROOT/linux/Campfire.Cli/bin/Release/net8.0/campfire.dll"
HOST_DIR="$HOME/cf-host"
GUEST_DIR="$HOME/cf-guest"

if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if [ ! -f "$CLI_DLL" ] || ! command -v dotnet >/dev/null 2>&1; then
    echo "✗ 环境没准备好，先跑第②步： curl <主机IP>:8000/step2.sh | bash" >&2
    exit 1
fi
if ! dotnet "$CLI_DLL" easytier check >/dev/null 2>&1; then
    echo "✗ EasyTier 组件没装好，先跑第②步。" >&2
    exit 1
fi

cf() { dotnet "$CLI_DLL" "$@"; }
ask() { read -r -p "$1" REPLY </dev/tty || REPLY=""; }
field() { sed -n "s/^[[:space:]]*\"$2\":[[:space:]]*\"\?\([^\",]*\)\"\?.*/\1/p" "$1" 2>/dev/null | head -1 || true; }

SESSION_PID=""
SESSION_DIR=""

cleanup() {
    if [ -n "$SESSION_DIR" ]; then
        cf --data-dir "$SESSION_DIR" stop >/dev/null 2>&1 || true
    fi
    sleep 1
    if [ -n "$SESSION_PID" ]; then kill "$SESSION_PID" 2>/dev/null || true; fi
}
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM

# 会话退出后把「为什么退出」摆到屏幕上，避免窗口滚过去查不到。
report_exit() {
    local dir="$1" log="$2" started="$3"
    echo ""
    echo "==== 会话进程已退出（运行约 $((SECONDS - started)) 秒）===="
    if [ -f "$log" ]; then
        echo "---- 控制台里的错误行 ----"
        grep -inE "联机失败|Link\.Error|Exception|failed|error|panic|拒绝|超时" "$log" | tail -15 || echo "  (无明显错误行)"
        echo "---- 控制台最后 15 行 ----"
        tail -15 "$log"
    fi
    local linklog="$dir/Link/link.log"
    if [ -f "$linklog" ]; then
        echo "---- Link/link.log 最后 12 行 ----"
        tail -12 "$linklog"
        echo "完整链路日志： $linklog"
    fi
    echo "控制台日志： $log"
}

# 后台跑 host/join，持续打印状态直到进程退出或用户 Ctrl+C。
run_monitored() {
    local dir="$1" log="$2"; shift 2
    SESSION_DIR="$dir"
    mkdir -p "$dir"
    : > "$log"
    local started=$SECONDS
    cf --data-dir "$dir" "$@" > "$log" 2>&1 &
    SESSION_PID=$!

    local code="" last_line="" shown_code=no ticks=0
    while kill -0 "$SESSION_PID" 2>/dev/null; do
        local snap="$dir/session.json"
        if [ -f "$snap" ]; then
            code="$(field "$snap" Code)"
            if [ "$shown_code" = no ] && [ -n "$code" ] && [ "$code" != "null" ]; then
                shown_code=yes
                echo ""
                echo "  ╔═══════════════════════════════════════════════════════╗"
                echo "  ║  邀请码： $code"
                echo "  ║  现在到 Windows 的 Campfire.exe 里手敲这个码加入。     ║"
                echo "  ╚═══════════════════════════════════════════════════════╝"
                echo "  （已写入 ~/cf-code.txt，可另开终端 cat 查看）"
                printf '%s\n' "$code" > "$HOME/cf-code.txt"
                echo ""
            fi
            local stage players direct relayed
            stage="$(field "$snap" Stage)"
            players="$(field "$snap" PlayerCount)"
            direct="$(field "$snap" DirectPeers)"
            relayed="$(field "$snap" RelayedPeers)"
            local line="阶段=$stage 玩家=${players:-0} 直连=${direct:-0} 中转=${relayed:-0}"
            if [ "$line" != "$last_line" ]; then
                echo "  [$(date +%H:%M:%S)] $line"
                last_line="$line"
            fi
        fi
        ticks=$((ticks + 1))
        if [ $((ticks % 5)) -eq 0 ]; then
            echo "  [$(date +%H:%M:%S)] 仍在运行（$((SECONDS - started))s）… Windows 端加入了没？"
        fi
        sleep 3
    done

    wait "$SESSION_PID" 2>/dev/null
    local rc=$?
    echo ""
    echo "  进程退出码：$rc"
    report_exit "$dir" "$log" "$started"
    SESSION_PID=""
}

echo "==== Campfire 第④步：Linux ⇄ 官方 Windows 客户端互通（v2 带日志留存）===="
echo "  官方客户端：C:\\Users\\yzhhh\\Downloads\\Campfire.exe  (v1.1.32，与源码同版本)"
echo ""
echo "  1) 方向 A · Linux 建房（房间层，--skip-probe，虚拟机不用装 MC）  ← 先做这个"
echo "  2) 方向 A · Linux 建房（正式联机，Linux 上要有 MC 并已对局域网开放）"
echo "  3) 方向 B · Linux 加入 Windows 官方客户端建的房（要输它的邀请码）"
echo ""
ask "选 1 / 2 / 3 [1]: "
MODE="${REPLY:-1}"

case "$MODE" in
1|2)
    ask "房主端口（正式联机填 MC「对局域网开放」显示的端口）[43210]: "
    PORT="${REPLY:-43210}"
    case "$PORT" in ''|*[!0-9]*) echo "✗ 端口必须是数字"; exit 1;; esac
    SKIP="--skip-probe"
    [ "$MODE" = "2" ] && SKIP=""
    echo ""
    echo "  启动 Linux 房主，端口 $PORT，日志 ~/cf-host.log"
    echo "  Windows 端加入后，下面每 3 秒刷新一次阶段/玩家数；Ctrl+C 结束。"
    echo ""
    # shellcheck disable=SC2086
    run_monitored "$HOST_DIR" "$HOME/cf-host.log" host --port "$PORT" --player LinuxHost $SKIP
    echo ""
    echo "  方向 A 判定：监控里是否出现 玩家=2 / 直连=1？Windows 端是否显示「已加入房间」？"
    echo "  若 Windows 端提示未找到游戏，先在 Windows 上开 MC 并点「对局域网开放」。"
    ;;
3)
    echo ""
    echo "  先在 Windows 官方客户端里建房，把它的邀请码抄下来（U/XXXX-XXXX-XXXX-XXXX）。"
    ask "输入 Windows 客户端的邀请码: "
    CODE="$(printf '%s' "$REPLY" | tr -d '[:space:]')"
    if [ -z "$CODE" ]; then echo "✗ 没输入邀请码"; exit 1; fi
    if ! cf code --check "$CODE" >/dev/null 2>&1; then
        echo "✗ 邀请码格式/校验位不对：$CODE"
        cf code --check "$CODE" || true
        exit 1
    fi
    echo "  邀请码有效，正在加入，日志 ~/cf-guest.log …"
    run_monitored "$GUEST_DIR" "$HOME/cf-guest.log" join --code "$CODE" --player LinuxGuest --skip-probe
    echo ""
    echo "  方向 B 判定：监控里是否出现 阶段=Ready / 玩家=2？控制台是否打印「已加入房间」？"
    ;;
*)
    echo "✗ 无效选择：$MODE"; exit 1;;
esac

echo ""
echo "==== 两个方向都通过，才算阶段 1 完成 ===="
echo "  方向 A：Linux 建房 → Windows 官方客户端用邀请码加入   [ ]"
echo "  方向 B：Windows 建房 → Linux 用邀请码加入             [ ]"
echo "  把上面整段输出（尤其「会话进程已退出」那段）截图发我，就能定位自动停止的原因。"
