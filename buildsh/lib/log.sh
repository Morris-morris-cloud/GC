#!/bin/sh
# buildsh/lib/log.sh —— 统一输出层
#
# 设计：默认只显示「步骤 / 结论」，细节进日志文件；需要细节时开 GC_VERBOSE=1。
#
#   GC_VERBOSE=1   额外显示细节（日志尾部、跳过项、产物路径）
#   GC_QUIET=1     只显示警告与错误（后台任务用，避免打乱进度条）
#   NO_COLOR=1     关闭颜色（写在前面：重定向到文件时自动关闭）
#
# 提供：
#   say <文本>            主步骤         ==> 文本
#   step_ok <文本>        步骤完成         ✓ 文本
#   note <文本>           一般信息（缩进）
#   detail <文本>         细节（仅 GC_VERBOSE）
#   warn / err <文本>     警告 / 错误（不受 GC_QUIET 影响）
#   run_logged <日志> <标签> <命令...>
#                         输出全部进日志；失败时回显尾部，成功时静默

GC_VERBOSE=${GC_VERBOSE:-0}
GC_QUIET=${GC_QUIET:-0}
GC_ROOT=${GC_ROOT:-}

if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
  _C_STEP=$(printf '\033[36m')
  _C_OK=$(printf '\033[32m')
  _C_WARN=$(printf '\033[33m')
  _C_ERR=$(printf '\033[31m')
  _C_DIM=$(printf '\033[2m')
  _C_OFF=$(printf '\033[0m')
else
  _C_STEP=''; _C_OK=''; _C_WARN=''; _C_ERR=''; _C_DIM=''; _C_OFF=''
fi

# 路径转相对（日志里少一长串前缀）
_rel() {
  if [ -n "$GC_ROOT" ]; then printf '%s' "${1#"$GC_ROOT"/}"; else printf '%s' "$1"; fi
}

say()     { [ "$GC_QUIET" = 1 ] && return 0; printf '%s==>%s %s\n' "$_C_STEP" "$_C_OFF" "$*"; }
step_ok() { [ "$GC_QUIET" = 1 ] && return 0; printf '%s  ✓%s %s\n' "$_C_OK" "$_C_OFF" "$*"; }
note()    { [ "$GC_QUIET" = 1 ] && return 0; printf '    %s\n' "$*"; }
detail()  { [ "$GC_VERBOSE" = 1 ] && [ "$GC_QUIET" != 1 ] || return 0
            printf '%s    %s%s\n' "$_C_DIM" "$*" "$_C_OFF"; }
warn()    { printf '%s  !!%s %s\n' "$_C_WARN" "$_C_OFF" "$*" >&2; }
err()     { printf '%s  xx%s %s\n' "$_C_ERR" "$_C_OFF" "$*" >&2; }

# 跑命令：输出全部写进日志文件。
#   - 成功：静默（GC_VERBOSE 时回显最后几行）
#   - 失败：回显日志尾部，让人不用翻文件也能看到原因
run_logged() {
  _rl_log="$1"; _rl_label="$2"; shift 2
  mkdir -p "$(dirname "$_rl_log")" 2>/dev/null || true
  "$@" >"$_rl_log" 2>&1
  _rl_st=$?
  if [ "$_rl_st" -ne 0 ]; then
    err "${_rl_label}失败（日志：$(_rel "$_rl_log")）"
    if [ -s "$_rl_log" ]; then
      # 只挑有信息量的行：跳过空行；超长行（编译器把整条命令行打进日志）截断，
      # 否则一屏全是 3000 字符的 swiftc 参数，真正的原因反而被淹掉。
      grep -vE '^[[:space:]]*$' "$_rl_log" | tail -12 \
        | cut -c1-200 | sed 's/^/     /' >&2
    fi
  else
    detail "${_rl_label}完成（$(_rel "$_rl_log")）"
  fi
  return "$_rl_st"
}

# 人类可读大小。不用 du：某些环境（沙箱/迷你工具箱）的 du 会报块占用而非文件大小
_hsize() {
  _hb=$(wc -c < "$1" 2>/dev/null | tr -d ' ')
  [ -n "$_hb" ] || _hb=0
  if [ "$_hb" -ge 1048576 ]; then printf '%dM' $((_hb / 1048576))
  elif [ "$_hb" -ge 1024 ]; then printf '%dK' $((_hb / 1024))
  else printf '%dB' "$_hb"; fi
}

# 计时（秒）：_t0=$(timer_start) ... timer_span "$_t0" -> 3m12s
timer_start() { date +%s; }
timer_span() {
  _ts=$(( $(date +%s) - $1 ))
  if [ "$_ts" -ge 60 ]; then printf '%dm%02ds' $((_ts / 60)) $((_ts % 60)); else printf '%ds' "$_ts"; fi
}
