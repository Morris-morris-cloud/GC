#!/bin/sh
# buildsh/build.sh —— 完整发布（原 publish）
#
# 目标平台按构建主机决定，每条产物都带 Flutter 前端：
#   macOS 主机 : mac-arm64（原生）+ win-amd64 / win-arm64（mingw 交叉）
#   Linux 主机 : linux-arm64（原生 / 交叉）+ win-amd64 / win-arm64（mingw 交叉）
#   Windows 主机: win-amd64 / win-arm64（原生）
#
# 前端是默认必需项：flutter 缺失或编译失败一律中断，不再悄悄产出只带 Avalonia
# 回退界面的包。确实要降级时用 ./build.sh build --allow-no-ui。
#
# 进度权重按实测耗时设定，供进度条估算总进度与剩余时间。

bs_build() {
  ROOT_DIR="$1"
  : "${APP_PROJ:?}" "${SLN:?}" "${VERSION:?}"

  HOST=$(uname -s)
  BUILD_MAC=0
  BUILD_LINUX=0
  case "$HOST" in
    Darwin) BUILD_MAC=1 ;;
    MINGW*|MSYS*|CYGWIN*) : ;;          # Windows 主机只能编 Windows
    *) BUILD_LINUX=1 ;;
  esac
  # Linux 目标已切成 arm64：Flutter 的 Linux 前端必须用 aarch64 主机自带的
  # engine 产物（官方只发 x64 版），x64 主机上强行编出来的是 x64 二进制，
  # 装到 arm 机器上跑不起来。这里直接跳过，并说明原因，不当成构建失败。
  if [ "$BUILD_LINUX" = 1 ]; then
    case "$(uname -m)" in
      aarch64|arm64) ;;
      *)
        BUILD_LINUX=0
        echo "!! 跳过 Linux 产物：目标为 linux-arm64，当前主机是 $(uname -m)"
        echo "   （Flutter Linux 引擎只有 x64 官方产物，需在 aarch64 主机上构建）"
        ;;
    esac
  fi

  # 显式降级：允许在没有该平台 Flutter 宿主的情况下仍出 Avalonia 版包
  ALLOW_NO_UI="${GC_ALLOW_NO_UI:-0}"

  # 权重：清理 版本 Mac WinX64 WinArm64 Linux 前端 骨架 打包 收尾
  WEIGHTS="2 3"
  [ "$BUILD_MAC" = 1 ] && WEIGHTS="$WEIGHTS 35"
  WEIGHTS="$WEIGHTS 25 25"
  [ "$BUILD_LINUX" = 1 ] && WEIGHTS="$WEIGHTS 30"
  WEIGHTS="$WEIGHTS 25 3 5 2"
  TOTAL=0
  for _w in $WEIGHTS; do TOTAL=$((TOTAL + _w)); done

  progress_init "$TOTAL" $WEIGHTS

  progress_step "清理旧产物"; progress_tick 100
  rm -rf "$ROOT_DIR/dist"; mkdir -p "$ROOT_DIR/dist"
  # 上次打包挪出来的中间产物（.app 目录，体积大、文件多）直接移出 build，
  # 不做递归删除：避免动辄几百个文件的不可逆删除。
  if [ -d "$ROOT_DIR/build/trash" ]; then
    mv "$ROOT_DIR/build/trash" "${TMPDIR:-/tmp}/gc-trash-$(date +%s)" 2>/dev/null || true
  fi

  progress_step "版本戳"; progress_tick 100
  stamp_versions

  # 每个 publish 用后台任务包裹，边跑边按「已用时/预估用时」推进当前步骤进度
  _run_publish() {
    _label="$1"; _est="$2"; shift 2
    ( "$@" >"$ROOT_DIR/build/logs/publish-$_label.log" 2>&1 ) &
    _pid=$!
    _t0=$(date +%s)
    while kill -0 "$_pid" 2>/dev/null; do
      _elapsed=$(( $(date +%s) - _t0 ))
      _p=$(( _elapsed * 100 / _est ))
      [ "$_p" -gt 96 ] && _p=96
      progress_tick "$_p"
      sleep 1
    done
    wait "$_pid" || { echo; echo "!! $_label 编译失败，见 build/logs/publish-$_label.log"; return 1; }
    progress_tick 100
  }

  if [ "$BUILD_MAC" = 1 ]; then
    progress_step "编译 Mac"
    _run_publish mac 90 publish_rid "osx-arm64" "dist/mac-arm64" false || return 1
    publish_backend "osx-arm64" "dist/mac-arm64" || return 1
    # Flutter 的 macOS 产物必须用 Xcode 在本机编，Linux 上无法替代
    build_flutter_ui macos "dist/mac-arm64" || {
      echo "!! macOS 前端编译失败。macOS 版 Flutter 只能在 macOS 上编。"
      return 1
    }
  elif [ "$ALLOW_NO_UI" = 1 ]; then
    progress_step "编译 Mac"
    _run_publish mac 90 publish_rid "osx-arm64" "dist/mac-arm64" false || return 1
    publish_backend "osx-arm64" "dist/mac-arm64" || return 1
    echo "!! --allow-no-ui：macOS 包内不含 Flutter 前端，启动器将回退 Avalonia"
  fi

  progress_step "编译 Win-x64"
  _run_publish winx64 70 publish_rid "win-x64" "dist/win-amd64" false || return 1
  publish_backend "win-x64" "dist/win-amd64" || return 1

  progress_step "编译 Win-arm64"
  _run_publish winarm 70 publish_rid "win-arm64" "dist/win-arm64" false || return 1
  publish_backend "win-arm64" "dist/win-arm64" || return 1

  if [ "$BUILD_LINUX" = 1 ]; then
    progress_step "编译 Linux"
    _run_publish linux 70 publish_rid "linux-arm64" "dist/linux-arm64" false || return 1
    publish_backend "linux-arm64" "dist/linux-arm64" || return 1
    build_flutter_ui linux "dist/linux-arm64" || return 1
  fi

  # Windows 前端：Windows 主机走官方 toolchain；Linux/macOS 主机用 mingw-w64 +
  # wine 交叉编译（buildsh/cross）。只产出 x64 一份，两个包共用，
  # ARM64 上由系统模拟 x64 进程运行前端。
  progress_step "编译 Win 前端"
  case "$HOST" in
    MINGW*|MSYS*|CYGWIN*) build_flutter_ui windows "dist/win-flutter" || return 1 ;;
    *)                    build_flutter_ui_cross "dist/win-flutter" || return 1 ;;
  esac
  for label in win-amd64 win-arm64; do
    cp -R "$ROOT_DIR/dist/win-flutter/." "$ROOT_DIR/dist/$label/"
  done
  rm -rf "$ROOT_DIR/dist/win-flutter"

  progress_step "运行骨架"; progress_tick 100
  if [ -d "$ROOT_DIR/dist/mac-arm64" ]; then
    package_mac_app
  fi
  prepare_runtime_layout "dist/win-amd64"
  prepare_runtime_layout "dist/win-arm64"
  if [ "$BUILD_LINUX" = 1 ]; then
    prepare_runtime_layout "dist/linux-arm64"
  fi
  if [ -d "$ROOT_DIR/dist/mac-arm64/GC.app/Contents/MacOS" ]; then
    prepare_runtime_layout "dist/mac-arm64/GC.app/Contents/MacOS"
  fi

  progress_step "打包 zip"; progress_tick 100
  pack_launcher

  progress_step "收尾"; progress_tick 100
  progress_done
  echo "==> 完成，产物："
  ls -lh "$ROOT_DIR"/dist/*/GCL-"$VERSION"-*.zip 2>/dev/null || true
}
