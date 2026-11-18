#!/usr/bin/env bash
#
# 在 Linux / macOS 上交叉编译 Flutter Windows 桌面界面（GC-UI）。
#
#   ./buildsh/cross/build-ui-win-cross.sh [输出目录]
#
# 产物目录是可直接拷到 Windows 上运行的一整套：
#   GC-UI.exe  flutter_windows.dll  data/{icudtl.dat,app.so,flutter_assets/}
#
# 原理：
#   Flutter 的 Windows 外壳（runner + cpp_client_wrapper）是纯 Win32 + 标准 C++，
#   只通过 C ABI 调 flutter_windows.dll，所以 mingw-w64 就能编译。
#   唯一的 Windows 专属步骤是 AOT 快照生成（gen_snapshot.exe 是 PE 文件），
#   用 wine 执行即可，全流程不需要 Windows 机器或 MSVC。
#
# 依赖：
#   Linux  sudo apt install mingw-w64 cmake ninja-build wine unzip curl
#   macOS  brew install mingw-w64 cmake ninja && brew install --cask wine-stable
#
# 环境变量：
#   FLUTTER_BIN         flutter 可执行文件（默认 PATH 中的 flutter，要求 3.24+）
#   WIN_ENGINE_MIRROR   引擎产物来源（默认官方 GCS，国内可换 storage.flutter-io.cn）
#
set -euo pipefail

ROOT_DIR=$(cd "$(dirname "$0")/../.." && pwd)
UI_DIR="$ROOT_DIR/GC-UI/frontend"
OUT_DIR="${1:-$UI_DIR/build/windows/x64/runner/Release}"
BUILD_DIR="$UI_DIR/build/windows/x64/cross"

say()  { printf '\033[36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[33m!!\033[0m %s\n' "$*"; }
die()  { printf '\033[31mxx\033[0m %s\n' "$*" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || die "缺少 $1（$2）"; }

# ---------------------------------------------------------------- 工具链

MINGW_CXX=""
check_toolchain() {
  need cmake "Linux: apt install cmake；macOS: brew install cmake"
  need ninja "Linux: apt install ninja-build；macOS: brew install ninja"
  need unzip "Linux: apt install unzip"
  need curl  "Linux: apt install curl"

  for c in x86_64-w64-mingw32-g++-posix x86_64-w64-mingw32-g++; do
    if command -v "$c" >/dev/null 2>&1; then MINGW_CXX="$c"; break; fi
  done
  [ -n "$MINGW_CXX" ] || die "缺少 mingw-w64 C++ 编译器
  Linux: sudo apt install g++-mingw-w64-x86-64-posix
  macOS: brew install mingw-w64"

  command -v x86_64-w64-mingw32-windres >/dev/null 2>&1 || die "缺少 mingw-w64 资源编译器"

  # gen_snapshot.exe 靠 wine 执行；没有 wine 就只能出 debug 版（JIT）
  if command -v wine >/dev/null 2>&1 || command -v wine64 >/dev/null 2>&1; then
    WINE_BIN=$(command -v wine 2>/dev/null || command -v wine64)
  else
    die "缺少 wine（AOT 快照需要执行 Windows 版 gen_snapshot.exe）
  Linux: sudo apt install wine
  macOS: brew install --cask wine-stable"
  fi
}

# ---------------------------------------------------------------- Flutter

resolve_flutter() {
  FLUTTER_BIN="${FLUTTER_BIN:-flutter}"
  command -v "$FLUTTER_BIN" >/dev/null 2>&1 || die "未找到 flutter，可设置 FLUTTER_BIN=/path/to/flutter"

  local bin_path root target
  bin_path=$(command -v "$FLUTTER_BIN")
  while [ -L "$bin_path" ]; do
    target=$(readlink "$bin_path")
    case "$target" in
      /*) bin_path="$target" ;;
      *)  bin_path="$(cd "$(dirname "$bin_path")" && pwd)/$target" ;;
    esac
  done
  root=$(cd "$(dirname "$bin_path")/.." && pwd)

  [ -f "$root/bin/internal/engine.version" ] || die "$root 不像 Flutter SDK 根目录"
  FLUTTER_ROOT="$root"
  ENGINE_DIR="$root/bin/cache/artifacts/engine"
  ENGINE_HASH=$(tr -d '[:space:]' < "$root/bin/internal/engine.version")

  # pubspec 要求 Dart 3.5+（Flutter 3.24+），版本不对直接提醒
  local dart_ver
  dart_ver=$("$root/bin/cache/dart-sdk/bin/dart" --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1) || dart_ver=""
  say "Flutter SDK: $root（engine ${ENGINE_HASH:0:10}${dart_ver:+, dart $dart_ver}）"
}

# ---------------------------------------------------------------- 引擎产物

ENGINE_DIR=""   # 由 resolve_flutter 填充

fetch_engine() {
  local base="$MIRROR/flutter/$ENGINE_HASH"
  local cache="${TMPDIR:-/tmp}/gc-win-engine"
  mkdir -p "$cache"

  # 每个条目：远端相对路径 | 落地目录 | 落地后的标志文件
  local -a items=(
    "windows-x64-release/windows-x64-flutter.zip|$ENGINE_DIR/windows-x64-release|flutter_windows.dll.lib"
    "windows-x64/artifacts.zip|$ENGINE_DIR/windows-x64|frontend_server_aot.dart.snapshot"
    "windows-x64/flutter-cpp-client-wrapper.zip|$ENGINE_DIR/windows-x64/cpp_client_wrapper|core_implementations.cc"
  )

  local item remote dest marker zip
  for item in "${items[@]}"; do
    IFS='|' read -r remote dest marker <<<"$item"
    [ -f "$dest/$marker" ] && continue

    zip="$cache/$(echo "$remote" | tr '/' '_')"
    if [ ! -s "$zip" ]; then
      say "下载 $(basename "$remote")"
      curl -fL --retry 3 --retry-delay 2 -o "$zip.part" "$base/$remote" \
        || die "下载失败：$base/$remote
可设置 WIN_ENGINE_MIRROR=https://storage.flutter-io.cn/flutter_infra_release 走国内镜像"
      mv "$zip.part" "$zip"
    fi
    mkdir -p "$dest"
    unzip -oq "$zip" -d "$dest"
  done

  install_gen_snapshot_shim
}

# flutter_tools 会直接 exec $ENGINE_DIR/*/gen_snapshot（无 .exe 后缀）。
# 这里放一个调用 wine 的同名脚本，并给原本的 exe 加上 .win 后缀避免混淆。
install_gen_snapshot_shim() {
  local dirs=("$ENGINE_DIR/windows-x64-release" "$ENGINE_DIR/windows-x64")
  local dir shim
  for dir in "${dirs[@]}"; do
    [ -f "$dir/gen_snapshot.exe" ] || continue
    [ -f "$dir/gen_snapshot.exe.win" ] || mv "$dir/gen_snapshot.exe" "$dir/gen_snapshot.exe.win"

    shim="$dir/gen_snapshot"
    cat > "$shim" <<EOF
#!/bin/sh
# 由 buildsh/cross/build-ui-win-cross.sh 生成：用 wine 执行 Windows 版 gen_snapshot。
export WINEPREFIX="\${WINEPREFIX:-\$HOME/.wine64}"
export WINEDEBUG=-all
exec "$WINE_BIN" "$dir/gen_snapshot.exe.win" "\$@"
EOF
    chmod +x "$shim"
  done
}

# ---------------------------------------------------------------- 构建

build_assets_and_aot() {
  say "生成 Dart 资源与 AOT 快照"
  ( cd "$UI_DIR" && "$FLUTTER_BIN" --no-version-check --suppress-analytics pub get >/dev/null )

  local dart="$FLUTTER_ROOT/bin/cache/dart-sdk/bin/dart"
  local tool="$FLUTTER_ROOT/packages/flutter_tools/bin/tool_backend.dart"

  # tool_backend 是官方 CMake 调用的同一入口：解包引擎产物到 ephemeral、
  # 生成 flutter_assets、aot_elf_release。它末尾的收尾步骤在非 Windows host
  # 上可能非零退出，所以只校验产物是否齐备。
  ( cd "$UI_DIR" && \
    FLUTTER_ROOT="$FLUTTER_ROOT" \
    PROJECT_DIR="$UI_DIR" \
    FLUTTER_STORAGE_BASE_URL="${FLUTTER_STORAGE_BASE_URL:-$MIRROR}" \
    "$dart" "$tool" windows-x64 release ) || true

  [ -f "$UI_DIR/build/windows/app.so" ] || die "AOT 快照未生成（wine 执行 gen_snapshot 失败？）"
  [ -d "$UI_DIR/build/flutter_assets" ] || die "flutter_assets 未生成"
  say "AOT 快照 $(du -h "$UI_DIR/build/windows/app.so" | cut -f1)，资源 $(du -sh "$UI_DIR/build/flutter_assets" | cut -f1)"
}

build_cpp() {
  local ver major minor patch build_no
  read -r major minor patch build_no <<<"$(sed -n 's/^version:[[:space:]]*//p' "$UI_DIR/pubspec.yaml" \
      | head -1 | awk -F'[.+]' '{print $1+0, $2+0, $3+0, ($4==""?0:$4+0)}')"
  ver="${major}.${minor}.${patch}"

  say "编译 C++ 外壳（$(basename "$MINGW_CXX")）"
  cmake -S "$ROOT_DIR/buildsh/cross" -B "$BUILD_DIR" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$ROOT_DIR/buildsh/cross/toolchain-mingw.cmake" \
    -DCMAKE_BUILD_TYPE=Release \
    -DBINARY_NAME=GC-UI \
    -DFLUTTER_VERSION="$ver" \
    -DFLUTTER_VERSION_MAJOR="$major" \
    -DFLUTTER_VERSION_MINOR="$minor" \
    -DFLUTTER_VERSION_PATCH="$patch" \
    -DFLUTTER_VERSION_BUILD="$build_no" >/dev/null

  cmake --build "$BUILD_DIR" -- -j "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" \
    || die "C++ 编译失败"

  [ -f "$BUILD_DIR/GC-UI.exe" ] || die "未产出 GC-UI.exe"
}

stage() {
  local eph="$UI_DIR/windows/flutter/ephemeral"
  say "组装到 $OUT_DIR"
  rm -rf "$OUT_DIR"
  mkdir -p "$OUT_DIR/data"

  cp "$BUILD_DIR/GC-UI.exe"                     "$OUT_DIR/"
  cp "$eph/flutter_windows.dll"                 "$OUT_DIR/"
  cp "$eph/icudtl.dat"                          "$OUT_DIR/data/"
  cp "$UI_DIR/build/windows/app.so"             "$OUT_DIR/data/"
  cp -R "$UI_DIR/build/flutter_assets/."        "$OUT_DIR/data/flutter_assets/"

  say "完成：$(du -sh "$OUT_DIR" | cut -f1)"
  find "$OUT_DIR" -maxdepth 2 -mindepth 1 | sed "s|$OUT_DIR|  .|"
}

# ---------------------------------------------------------------- main

MIRROR="${WIN_ENGINE_MIRROR:-https://storage.googleapis.com/flutter_infra_release}"

check_toolchain
resolve_flutter
fetch_engine
build_assets_and_aot
build_cpp
stage
