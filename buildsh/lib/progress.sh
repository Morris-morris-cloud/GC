#!/bin/sh
# buildsh/lib/progress.sh —— 进度显示
#
# 终端：底部单行实时进度条  [####....] 编译Mac   45%  30%  1m20s
# 非终端（重定向 / CI / 日志）：逐行打印步骤，不打 \r，不刷屏
#
#   progress_init <总权重> <每步权重...>
#   progress_step <步骤名>
#   progress_tick <0-100>        # 当前步骤完成度
#   progress_done
#
# GC_NO_PROGRESS=1 可强制关闭进度条（但保留步骤行）

PROGRESS_TOTAL=1
PROGRESS_WEIGHTS=""
PROGRESS_IDX=0
PROGRESS_CUR=0
PROGRESS_STARTED=0
PROGRESS_TASK="准备"
_P_OVERALL=0
_P_ETA="--"

GC_PROGRESS_TTY=0
if [ -t 1 ] && [ -z "${GC_NO_PROGRESS:-}" ] && [ "${GC_QUIET:-0}" != 1 ]; then
  GC_PROGRESS_TTY=1
fi

# 一次算出整体百分比 + 剩余时间（原来每帧要循环两遍、fork 三次 date）
_progress_refresh() {
  _sum=0; _i=1
  for _w in $PROGRESS_WEIGHTS; do
    if [ "$_i" -lt "$PROGRESS_IDX" ]; then
      _sum=$((_sum + _w))
    elif [ "$_i" -eq "$PROGRESS_IDX" ]; then
      _sum=$((_sum + _w * PROGRESS_CUR / 100))
    fi
    _i=$((_i + 1))
  done
  _P_OVERALL=$((_sum * 100 / PROGRESS_TOTAL))
  [ "$_P_OVERALL" -gt 100 ] && _P_OVERALL=100

  _t=$(( $(date +%s) - PROGRESS_STARTED ))
  if [ "$_P_OVERALL" -le 3 ] || [ "$_t" -le 0 ]; then
    _sec=240                                    # 前期按经验值粗估 4 分钟
  else
    _sec=$(( _t * (100 - _P_OVERALL) / _P_OVERALL ))
  fi
  if [ "$_sec" -ge 60 ]; then
    _P_ETA="$((_sec / 60))m$((_sec % 60))s"
  else
    _P_ETA="${_sec}s"
  fi
}

_progress_bar() {
  _filled=$((_P_OVERALL * 20 / 100))
  _bar=""; _i=0
  while [ "$_i" -lt 20 ]; do
    if [ "$_i" -lt "$_filled" ]; then _bar="$_bar#"; else _bar="$_bar."; fi
    _i=$((_i + 1))
  done
  printf '%s' "$_bar"
}

progress_draw() {
  [ "$GC_PROGRESS_TTY" = 1 ] || return 0
  _progress_refresh
  printf '\r\033[K[%s] %-16s %3d%%  %3d%%  %s' \
    "$(_progress_bar)" "$PROGRESS_TASK" "$PROGRESS_CUR" "$_P_OVERALL" "$_P_ETA"
}

progress_init() {
  PROGRESS_TOTAL=$1; shift
  PROGRESS_WEIGHTS="$*"
  PROGRESS_IDX=0
  PROGRESS_CUR=0
  PROGRESS_STARTED=$(date +%s)
  PROGRESS_TASK="准备"
  progress_draw
}

progress_step() {
  PROGRESS_TASK="$1"
  PROGRESS_CUR=0
  PROGRESS_IDX=$((PROGRESS_IDX + 1))
  if [ "$GC_PROGRESS_TTY" = 1 ]; then
    progress_draw
  else
    # 非终端：一行一步，冒号后给个序号，日志里能看清阶段划分
    [ "${GC_QUIET:-0}" = 1 ] && return 0
    printf '==> [%d] %s\n' "$PROGRESS_IDX" "$1"
  fi
}

progress_tick() {
  # 非终端不需要刷新，直接返回（省掉每秒几次无用计算）
  [ "$GC_PROGRESS_TTY" = 1 ] || return 0
  PROGRESS_CUR=$1
  progress_draw
}

progress_done() {
  PROGRESS_CUR=100
  if [ "$GC_PROGRESS_TTY" = 1 ]; then
    progress_draw
    printf '\n'
  fi
}
