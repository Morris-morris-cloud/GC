#!/bin/sh
# buildsh/check.sh —— 验证：编译 + 自检 + 离屏测试 + 产物完整性
# 步骤权重：编译(40) 自检(15) 离屏(25) 产物(20)

bs_check() {
  ROOT_DIR="$1"
  progress_init 100 40 15 25 20
  _fail=0

  progress_step "编译检查"
  if dotnet build "$SLN" -c Debug -v quiet >"$ROOT_DIR/build/logs/check-build.log" 2>&1; then
    progress_tick 100
  else
    progress_tick 100; _fail=1
    echo; echo "!! 编译失败，见 build/logs/check-build.log"
  fi

  progress_step "模型自检"
  if EMS_SELFTEST=1 dotnet run --project "$APP_PROJ" -c Debug --no-build \
      >"$ROOT_DIR/build/logs/check-selftest.log" 2>&1; then
    grep -q "FAIL" "$ROOT_DIR/build/logs/check-selftest.log" && { _fail=1; echo; echo "!! 自检有失败项"; }
  else
    _fail=1; echo; echo "!! 自检异常"
  fi
  progress_tick 100

  progress_step "离屏渲染"
  if dotnet run --project "$TEST_PROJ" -c Debug --no-build -- \
      "$ROOT_DIR/build/screenshots" >"$ROOT_DIR/build/logs/check-render.log" 2>&1; then
    progress_tick 100
  else
    _fail=1; progress_tick 100; echo; echo "!! 离屏渲染失败"
  fi

  progress_step "产物检查"
  _n=$(ls "$ROOT_DIR"/dist/GC-"$VERSION"-*.zip 2>/dev/null | wc -l | tr -d ' ')
  if [ "$_n" -ge 3 ]; then
    progress_tick 100
  else
    _fail=1; progress_tick 100; echo; echo "!! 产物不足 3 个（当前 $_n）"
  fi

  progress_done
  [ "$_fail" -eq 0 ] && echo "==> 全部通过" || echo "==> 有失败项"
  return "$_fail"
}
