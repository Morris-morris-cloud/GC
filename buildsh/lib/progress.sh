#!/bin/sh
# buildsh/lib/progress.sh —— apt 风格底部实时进度条
#
# 底部一行：任务  当前任务进度  总进度  剩余时间
#  编译MacOS 95% 30% 10s
#
# 用法：
#   progress_init <总步骤数> <每步权重...>
#   progress_step <步骤名>              # 开始某步骤
#   progress_tick <0-100>              # 更新当前步骤完成度
#   progress_done                      # 结束

PROGRESS_TOTAL=1
PROGRESS_WEIGHTS=""
PROGRESS_IDX=0
PROGRESS_CUR=0
PROGRESS_STARTED=0
PROGRESS_LAST=0
PROGRESS_HISTORY=""

_now() { date +%s; }

# 加权总进度：前序步骤权重之和 + 当前步骤权重*完成度
_progress_overall() {
  _sum=0; _i=0
  for _w in $PROGRESS_WEIGHTS; do
    if [ "$_i" -lt "$PROGRESS_IDX" ]; then _sum=$((_sum + _w)); fi
    _i=$((_i + 1))
  done
  _cw=$(echo "$PROGRESS_WEIGHTS" | awk -v i="$((PROGRESS_IDX+1))" '{print $i}')
  [ -z "$_cw" ] && _cw=1
  _sum=$((_sum + _cw * PROGRESS_CUR / 100))
  echo $((_sum * 100 / PROGRESS_TOTAL))
}

# 剩余时间：基于「已完成总进度 / 已用时间」线性外推
_progress_eta() {
  _p=$(_progress_overall)
  [ "$_p" -le 0 ] && { echo "--"; return; }
  _t=$(( $(_now) - PROGRESS_STARTED ))
  [ "$_t" -le 0 ] && { echo "--"; return; }
  _eta=$(( _t * (100 - _p) / _p ))
  if [ "$_eta" -ge 60 ]; then echo "$(( _eta / 60 ))m$((_eta % 60))s"; else echo "${_eta}s"; fi
}

progress_draw() {
  _task="$PROGRESS_TASK"
  _cur=$PROGRESS_CUR
  _overall=$(_progress_overall)
  _eta=$(_progress_eta)

  # 进度条（20 格）
  _filled=$((_overall * 20 / 100))
  _bar=""
  _i=0
  while [ "$_i" -lt 20 ]; do
    if [ "$_i" -lt "$_filled" ]; then _bar="${_bar}#"; else _bar="${_bar}."; fi
    _i=$((_i + 1))
  done

  # \r 回到行首，用 \033[K 清到行尾；底部固定一行
  printf '\r\033[K[%s] %-16s %3d%%  总 %3d%%  %s' \
    "$_bar" "$_task" "$_cur" "$_overall" "$_eta"
}

progress_init() {
  PROGRESS_TOTAL=$1
  shift
  PROGRESS_WEIGHTS="$*"
  PROGRESS_IDX=0
  PROGRESS_CUR=0
  PROGRESS_STARTED=$(_now)
  PROGRESS_TASK="准备"
  progress_draw
}

progress_step() {
  PROGRESS_TASK="$1"
  PROGRESS_CUR=0
  PROGRESS_IDX=$((PROGRESS_IDX + 1))
  progress_draw
}

progress_tick() {
  PROGRESS_CUR=$1
  progress_draw
}

progress_done() {
  PROGRESS_CUR=100
  progress_tick 100
  printf '\n'
}
