#!/bin/sh
# buildsh/build.sh —— 完整发布（原 publish）
# 步骤及权重按实测耗时设定，供进度条估算总进度与剩余时间：
#   清理(2) 版本(3) Mac(35) WinX64(25) WinArm(25) 骨架(3) 打包(5) 收尾(2) = 100

bs_build() {
  ROOT_DIR="$1"
  : "${APP_PROJ:?}" "${SLN:?}" "${VERSION:?}"

  progress_init 100 2 3 35 25 25 3 5 2

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

  progress_step "编译 Mac"
  _run_publish mac 90 publish_rid "osx-arm64" "dist/mac-arm64" false || return 1
  publish_backend "osx-arm64" "dist/mac-arm64" || return 1
  # Flutter 的 macOS 产物只能在 macOS 上编；交叉构建时跳过，包内回退内置界面
  if [ "$(uname -s)" = "Darwin" ]; then
    build_flutter_ui macos "dist/mac-arm64" || return 1
  fi

  progress_step "编译 Win-x64"
  _run_publish winx64 70 publish_rid "win-x64" "dist/win-amd64" true || return 1
  publish_backend "win-x64" "dist/win-amd64" || return 1

  progress_step "编译 Win-arm64"
  _run_publish winarm 70 publish_rid "win-arm64" "dist/win-arm64" true || return 1
  publish_backend "win-arm64" "dist/win-arm64" || return 1

  # Windows 版 Flutter 只能在 Windows 上编；x64 产物同时放进两个 Windows 包
  # （ARM64 上由系统模拟 x64 进程运行前端）。
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
      build_flutter_ui windows "dist/win-flutter" || return 1
      for label in win-amd64 win-arm64; do
        cp -R "$ROOT_DIR/dist/win-flutter/." "$ROOT_DIR/dist/$label/"
      done
      rm -rf "$ROOT_DIR/dist/win-flutter"
      ;;
  esac

  progress_step "运行骨架"; progress_tick 100
  package_mac_app
  prepare_runtime_layout "dist/win-amd64"
  prepare_runtime_layout "dist/win-arm64"
  prepare_runtime_layout "dist/mac-arm64/GC.app/Contents/MacOS"

  progress_step "打包 zip"; progress_tick 100
  pack_launcher

  progress_step "收尾"; progress_tick 100
  progress_done
  echo "==> 完成，产物："
  ls -lh "$ROOT_DIR"/dist/*/GC-"$VERSION"-*.zip 2>/dev/null || true
}
