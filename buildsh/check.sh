#!/bin/sh
# buildsh/check.sh —— 验证：编译 + 产物完整性
#
# 原先的「模型自检」和「离屏渲染」两项都依赖 Avalonia 界面与截图工程；
# 界面与模型渲染迁到 Flutter 之后它们已经没有意义 —— 前端正确性交给
# `flutter analyze`，这里只保证 C# 侧编译干净、发布包齐备。
#
# 步骤权重：编译(60) 产物(40)

bs_check() {
  ROOT_DIR="$1"
  GC_ROOT="${GC_ROOT:-$ROOT_DIR}"; export GC_ROOT
  mkdir -p "$GC_ROOT/build/logs"

  progress_init 100 60 40
  _fail=0

  progress_step "编译"
  if run_logged "$GC_ROOT/build/logs/check-build.log" "解决方案编译 " \
       dotnet build "$SLN" -c Release --nologo -v:q; then
    progress_tick 100
  else
    _fail=1; progress_tick 100
  fi

  progress_step "产物"
  _n=0
  for _z in "$ROOT_DIR"/dist/*/GCL-"$VERSION"-*.zip; do
    [ -f "$_z" ] && _n=$((_n + 1))
  done
  if [ "$_n" -ge 1 ]; then
    detail "共 ${_n} 个发布包"
  else
    _fail=1; warn "dist 下没有发布包（先跑 ./build.sh build）"
  fi
  progress_tick 100

  progress_done
  if [ "$_fail" -eq 0 ]; then
    step_ok "全部通过"
  else
    err "有失败项"
  fi
  return "$_fail"
}
