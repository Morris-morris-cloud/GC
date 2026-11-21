#!/bin/sh
# buildsh/lib/flutter.sh —— Flutter 前端构建与落位
#
# 分两步，便于和其它编译并行：
#   flutter_build <平台>       只编译，产物留在 GC-UI/frontend/build/
#   flutter_stage <平台> <目录> 把产物铺进打包目录
#
# 输出策略：flutter 的输出（尤其 macOS 上 flutter_inappwebview 那几百行 Swift
# 弃用警告）全部进 build/logs/flutter-<平台>.log，成功时不显示，失败才回显尾部。

_ui_src() { printf '%s' "$GC_ROOT/GC-UI/frontend"; }

# pub get 只在 pubspec 变了才跑：package_config.json 比 pubspec.yaml / pubspec.lock
# 都新就说明依赖已就绪，省掉每次构建的解析与 "Changed N dependencies!" 抖动
_flutter_deps_ready() {
  _pc="$(_ui_src)/.dart_tool/package_config.json"
  [ -f "$_pc" ] && [ -f "$(_ui_src)/pubspec.lock" ] &&
    [ "$_pc" -nt "$(_ui_src)/pubspec.yaml" ] && [ "$_pc" -nt "$(_ui_src)/pubspec.lock" ]
}

_flutter() { ( cd "$(_ui_src)" && "$FLUTTER_BIN" "$@" ); }

flutter_build() {
  _plat="$1"
  UI_SRC="$(_ui_src)"
  FLUTTER_BIN="${FLUTTER_BIN:-flutter}"

  [ -d "$UI_SRC" ] || { err "找不到 Flutter 工程：GC-UI/frontend"; return 1; }
  command -v "$FLUTTER_BIN" >/dev/null 2>&1 ||
    { err "未找到 flutter（可用 FLUTTER_BIN=<路径> 指定）"; return 1; }

  say "编译 Flutter 前端（${_plat}）"
  if _flutter_deps_ready; then
    detail "依赖已就绪，跳过 pub get"
  else
    run_logged "$GC_ROOT/build/logs/flutter-pubget.log" "pub get " \
      _flutter pub get || return 1
  fi

  # 注意：macOS 的 `flutter build macos` 没有 --no-codesign 选项（那是 iOS 的），
  # 传了会直接报 "Could not find an option named no-codesign"。
  # macOS 模板默认用 ad-hoc 签名（CODE_SIGN_IDENTITY = "-"），无证书的 CI 也能构建出 .app，
  # 只是首次打开需要右键「打开」。
  run_logged "$GC_ROOT/build/logs/flutter-$_plat.log" "Flutter $_plat 构建 " \
    _flutter build "$_plat" --release || return 1
  step_ok "Flutter ${_plat}"
}

# flutter_stage <平台> <目标目录（绝对路径）>
flutter_stage() {
  _plat="$1"; _dest="$2"
  UI_SRC="$(_ui_src)"
  mkdir -p "$_dest"

  case "$_plat" in
    windows)
      # Release/ 里含 GC-UI.exe、flutter_windows.dll 和 data/
      cp -R "$UI_SRC/build/windows/x64/runner/Release/." "$_dest/" || return 1
      ;;
    macos)
      _app="$UI_SRC/build/macos/Build/Products/Release/GC-UI.app"
      [ -d "$_app" ] || { err "找不到 Flutter macOS 产物（GC-UI.app）"; return 1; }
      _trash "$_dest/GC-UI.app"
      cp -R "$_app" "$_dest/" || return 1
      ;;
    linux)
      _bundle=""
      for _cand in "$UI_SRC/build/linux/arm64/release/bundle" \
                   "$UI_SRC/build/linux/x64/release/bundle"; do
        [ -d "$_cand" ] && _bundle="$_cand" && break
      done
      [ -n "$_bundle" ] ||
        { err "找不到 Flutter Linux bundle（期望 build/linux/{arm64,x64}/release/bundle）"; return 1; }
      # 目标包是 arm64 却只编出 x64：主机架构不对，宁可失败也不出坏包
      case "$_bundle" in
        */arm64/*) ;;
        *) [ "$(uname -m)" = aarch64 ] || [ "$(uname -m)" = arm64 ] ||
             { err "Linux 包目标 arm64，但当前主机 $(uname -m) 只能编出 x64 前端"; return 1; } ;;
      esac
      cp -R "$_bundle/." "$_dest/" || return 1
      ;;
    *)
      err "不支持的 Flutter 目标平台：$_plat"; return 1
      ;;
  esac
  detail "$(_rel "$_dest")（Flutter ${_plat}）"
}

# 兼容旧调用：编译 + 落位一起做。DEST 可为相对仓库根的路径。
build_flutter_ui() {
  _plat="$1"; _dest="$2"
  case "$_dest" in
    /*) : ;;
    *) _dest="$GC_ROOT/$_dest" ;;
  esac
  flutter_build "$_plat" || return 1
  flutter_stage "$_plat" "$_dest"
}

# 在 Linux / macOS 上交叉编译 Windows 前端：mingw-w64 编 C++ 外壳，
# wine 跑 Windows 版 gen_snapshot 生成 AOT 快照。产物布局与官方 toolchain 一致。
build_flutter_ui_cross() {
  _dest="$1"
  UI_SRC="$(_ui_src)"
  FLUTTER_BIN="${FLUTTER_BIN:-flutter}"
  [ -d "$UI_SRC" ] || { err "找不到 Flutter 工程：GC-UI/frontend"; return 1; }

  _log="$GC_ROOT/build/logs/flutter-win-cross.log"
  say "交叉编译 Windows 前端（mingw-w64 + wine）"
  # 交叉脚本只接受官方默认输出位置，先按官方 toolchain 编一份骨架（依赖它下载引擎产物）
  FLUTTER_BIN="$FLUTTER_BIN" \
    run_logged "$_log" "Windows 前端 " \
      bash "$GC_ROOT/buildsh/cross/build-ui-win-cross.sh" \
        "$UI_SRC/build/windows/x64/runner/Release" || return 1

  mkdir -p "$_dest"
  cp -R "$UI_SRC/build/windows/x64/runner/Release/." "$_dest/" || return 1
  step_ok "Windows 前端（交叉编译）"
}
