#!/usr/bin/env bash
# Campfire Linux port — 自建大厅服务端契约验收脚本（基于 Xocio/Campfire v1.1.32 GPL-3.0 的 LobbyClient.cs）。
# 用法：
#   1) 先启动服务端：./run-lobby.sh
#   2) 再跑验收：    ./verify-lobby.sh
# 可用环境变量覆盖：BASE / DOTNET / CLI_DLL / ADMIN_TOKEN
# 不依赖 jq；只用 curl + sed + grep，Windows Git Bash 与 Linux 都能跑。

set -uo pipefail

BASE="${BASE:-http://127.0.0.1:5080}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_DLL="${CLI_DLL:-$SCRIPT_DIR/Campfire.Cli/bin/Release/net8.0/campfire.dll}"
DOTNET="${DOTNET:-dotnet}"
ADMIN_TOKEN="${ADMIN_TOKEN:-}"
OWNER=0123456789abcdef0123456789abcdef

PASS=0
FAIL=0
SKIP=0
ok()   { echo "  [PASS] $1"; PASS=$((PASS + 1)); }
no()   { echo "  [FAIL] $1"; FAIL=$((FAIL + 1)); }
skip() { echo "  [SKIP] $1"; SKIP=$((SKIP + 1)); }

# call <METHOD> <PATH> [BODY] [BEARER_TOKEN] -> 输出 "响应体\nHTTP状态码"
call() {
    local method="$1" path="$2" body="${3:-}" token="${4:-}"
    local -a args=(-s -w '\n%{http_code}' -X "$method" "$BASE$path" -H 'Content-Type: application/json')
    [ -n "$body" ]  && args+=(-d "$body")
    [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
    curl "${args[@]}"
}
status_of() { echo "$1" | tail -n 1; }
body_of()   { echo "$1" | sed '$d'; }
expect_status() {
    if [ "$(status_of "$2")" = "$3" ]; then ok "$1"; else
        no "$1（期望 $3，实际 $(status_of "$2")：$(body_of "$2")）"
    fi
}
expect_has() {
    case "$2" in *"$3"*) ok "$1";; *) no "$1（响应里没有 $3）";; esac
}
json_field() { echo "$1" | sed -n "s/.*\"$2\":\"\([^\"]*\)\".*/\1/p"; }

newcode() {
    if [ -f "$CLI_DLL" ]; then
        "$DOTNET" "$CLI_DLL" code
    else
        echo ""
    fi
}
publish() { # publish <code> <title> [password] -> 完整响应
    local password="${3:-}"
    local extra=""
    [ -n "$password" ] && extra=",\"password\":\"$password\""
    call POST /v1/lobby/rooms \
        "{\"owner\":\"$OWNER\",\"app\":\"1.1.32\",\"code\":\"$1\",\"title\":\"$2\",\"players\":1$extra}"
}

echo "验收目标 : $BASE"
echo "CLI 程序 : $CLI_DLL"
echo ""

# ---------- 0. 可达性与降级 ----------
HEAD=$(call GET /v1/lobby)
expect_status "GET /v1/lobby 返回 200" "$HEAD" 200
expect_has   "列表含 enabled 字段" "$(body_of "$HEAD")" '"enabled"'
MANIFEST=$(call GET /v1/manifest)
expect_status "未实现的 /v1/manifest 返回 404" "$MANIFEST" 404

if [ ! -f "$CLI_DLL" ]; then
    skip "以下需要 campfire.dll 的用例（先跑 ./build.sh）"
    echo ""
    echo "通过 $PASS，失败 $FAIL，跳过 $SKIP"
    [ "$FAIL" -eq 0 ] || exit 1
    exit 0
fi

# ---------- 1. 发布 / 列表 / 心跳 / 换码 ----------
C1=$(newcode)
R=$(publish "$C1" "Verify Room")
expect_status "POST publish 返回 200" "$R" 200
BODY=$(body_of "$R")
ID=$(json_field "$BODY" id)
TOKEN=$(json_field "$BODY" token)
expect_has "响应含 id" "$BODY" '"id":"'
expect_has "响应含 token" "$BODY" '"token":"'
expect_has "响应含 heartbeatSeconds" "$BODY" '"heartbeatSeconds"'

LIST=$(call GET /v1/lobby)
expect_status "GET 列表返回 200" "$LIST" 200
expect_has   "列表能看到刚发布的房间 id" "$(body_of "$LIST")" "$ID"
case "$(body_of "$LIST")" in *"\"code\""*) no "列表泄露邀请码原文";; *) ok "列表不含邀请码原文";; esac

expect_status "PUT 心跳无 token 返回 401" \
    "$(call PUT "/v1/lobby/rooms/$ID" '{"players":2,"maxPlayers":8,"voice":true}')" 401
HB=$(call PUT "/v1/lobby/rooms/$ID" '{"players":2,"maxPlayers":8,"voice":true}' "$TOKEN")
expect_status "PUT 心跳带 token 返回 200" "$HB" 200
expect_has   "心跳返回 heartbeatSeconds" "$(body_of "$HB")" '"heartbeatSeconds"'

JOIN=$(call POST "/v1/lobby/rooms/$ID/join" '{}')
expect_status "POST join 返回 200" "$JOIN" 200
expect_has   "join 下发的邀请码与发布时一致" "$(body_of "$JOIN")" "$C1"

# ---------- 2. 错误码矩阵 ----------
expect_status "重复公开同一邀请码返回 409" "$(publish "$C1" "Duplicate")" 409
expect_has   "409 错误体含 already listed" "$(body_of "$(publish "$C1" "Duplicate 2")")" '"already listed"'

MIN=$(json_field "$(body_of "$HEAD")" minAppVersion)
if [ -n "$MIN" ]; then
    expect_status "低于 minAppVersion 返回 426" \
        "$(call POST /v1/lobby/rooms "{\"owner\":\"$OWNER\",\"app\":\"1.1.0\",\"code\":\"$(newcode)\",\"title\":\"Old Client\"}")" 426
else
    skip "426 版本门禁（服务端未配置 minAppVersion）"
fi

BAD=$(call POST /v1/lobby/rooms "{\"owner\":\"$OWNER\",\"app\":\"1.1.32\",\"code\":\"U/!!!!-!!!!-!!!!-!!!!\",\"title\":\"Bad Code\"}")
expect_status "非法邀请码返回 422" "$BAD" 422
expect_has   "422 指出 field=code" "$(body_of "$BAD")" '"field":"code"'

NOTITLE=$(call POST /v1/lobby/rooms "{\"owner\":\"$OWNER\",\"app\":\"1.1.32\",\"code\":\"$(newcode)\"}")
expect_status "缺标题返回 422" "$NOTITLE" 422
expect_has   "422 指出 field=title" "$(body_of "$NOTITLE")" '"field":"title"'

expect_status "DELETE 错误 token 返回 401" \
    "$(call DELETE "/v1/lobby/rooms/$ID" "" "wrong-token")" 401
expect_status "DELETE 正确 token 返回 200" \
    "$(call DELETE "/v1/lobby/rooms/$ID" "" "$TOKEN")" 200
expect_status "撤下后 join 返回 404" \
    "$(call POST "/v1/lobby/rooms/$ID/join" '{}')" 404

# ---------- 3. 密码房间 ----------
C2=$(newcode)
RP=$(publish "$C2" "Locked Room" "s3cret")
expect_status "发布带密码房间返回 200" "$RP" 200
PBODY=$(body_of "$RP")
PID=$(json_field "$PBODY" id)
PTOK=$(json_field "$PBODY" token)
NO_PW=$(call POST "/v1/lobby/rooms/$PID/join" '{}')
expect_status "无密码 join 返回 401" "$NO_PW" 401
expect_has   "错误体为 password required" "$(body_of "$NO_PW")" '"password required"'
WRONG_PW=$(call POST "/v1/lobby/rooms/$PID/join" '{"password":"nope"}')
expect_status "错密码 join 返回 401" "$WRONG_PW" 401
expect_has   "错误体为 wrong password" "$(body_of "$WRONG_PW")" '"wrong password"'
GOOD_PW=$(call POST "/v1/lobby/rooms/$PID/join" '{"password":"s3cret"}')
expect_status "正确密码 join 返回 200" "$GOOD_PW" 200
expect_has   "拿到与发布一致的邀请码" "$(body_of "$GOOD_PW")" "$C2"
call DELETE "/v1/lobby/rooms/$PID" "" "$PTOK" > /dev/null

# ---------- 4. 举报隐藏 410 ----------
C3=$(newcode)
RR=$(publish "$C3" "Reported Room")
RID=$(json_field "$(body_of "$RR")" id)
for _ in 1 2 3 4 5; do
    call POST "/v1/lobby/rooms/$RID/report" '{"reason":"content"}' > /dev/null
done
REPORTED=$(call POST "/v1/lobby/rooms/$RID/join" '{}')
if [ "$(status_of "$REPORTED")" = "410" ]; then
    ok "举报达阈值后 join 返回 410 reported"
else
    no "举报阈值未生效（实际 $(status_of "$REPORTED")，检查 Lobby__ReportThreshold）"
fi

# ---------- 5. 管理员下架 410（需服务端配 AdminToken） ----------
if [ -n "$ADMIN_TOKEN" ]; then
    C4=$(newcode)
    RM=$(publish "$C4" "Removed Room")
    MID=$(json_field "$(body_of "$RM")" id)
    REMOVED=$(curl -s -w '\n%{http_code}' -X POST "$BASE/v1/lobby/rooms/$MID/remove" \
                   -H "X-Admin-Token: $ADMIN_TOKEN")
    expect_status "管理员下架返回 200" "$REMOVED" 200
    AFTER=$(call POST "/v1/lobby/rooms/$MID/join" '{}')
    expect_status "下架后 join 返回 410" "$AFTER" 410
    expect_has   "错误体为 removed" "$(body_of "$AFTER")" '"removed"'
else
    skip "410 removed（启动时设置 Lobby__AdminToken 并给本脚本 ADMIN_TOKEN）"
fi

# ---------- 6. CLI 客户端闭环（用官方 LobbyClient 解析服务端 JSON） ----------
CLI_OUT=$("$DOTNET" "$CLI_DLL" --service-url "$BASE" lobby list 2>&1)
if [ $? -eq 0 ]; then ok "campfire lobby list 解析成功"; else no "campfire lobby list 失败：$CLI_OUT"; fi
expect_has "CLI 输出含房间数" "$CLI_OUT" '房间数'

C5=$(newcode)
PUBLISH_OUT=$("$DOTNET" "$CLI_DLL" --service-url "$BASE" lobby publish --code "$C5" --title 'CLI Verify' --once 2>&1)
if [ $? -eq 0 ]; then ok "campfire lobby publish --once 成功"; else no "publish --once 失败：$PUBLISH_OUT"; fi
expect_has "publish --once 完成撤下" "$PUBLISH_OUT" '已从大厅撤下'

C6=$(newcode)
R6=$(publish "$C6" "CLI Join Target")
ID6=$(json_field "$(body_of "$R6")" id)
JOIN_OUT=$("$DOTNET" "$CLI_DLL" --service-url "$BASE" lobby join "$ID6" 2>&1)
if [ $? -eq 0 ]; then ok "campfire lobby join 换取邀请码成功"; else no "lobby join 失败：$JOIN_OUT"; fi
expect_has "CLI 输出含邀请码" "$JOIN_OUT" "$C6"
TOK6=$(json_field "$(body_of "$R6")" token)
call DELETE "/v1/lobby/rooms/$ID6" "" "$TOK6" > /dev/null

echo ""
echo "通过 $PASS，失败 $FAIL，跳过 $SKIP"
[ "$FAIL" -eq 0 ] || exit 1
echo "LOBBY VERIFY OK"
