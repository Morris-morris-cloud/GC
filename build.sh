#!/bin/sh
# ---------------------------------------------------------------------------
# GC 构建入口（薄封装）
#
#   ./build.sh build                        完整发布（当前主机对应的全部平台）
#   ./build.sh build --targets=mac          只出 macOS（跳过 Windows 交叉编译）
#   ./build.sh build --targets=win,linux    只出 Windows / Linux
#   ./build.sh build -version=1.1.3         指定版本号（写回 Directory.Build.props）
#   ./build.sh build --allow-no-ui          允许不带 Flutter 前端（回退 Avalonia）
#   ./build.sh check                        验证：编译 + 自检 + 离屏渲染 + 产物
#   ./build.sh models                       编译模型 dll + 封面 + 版本戳
#   ./build.sh stamp                        重新生成 version.json
#   ./build.sh run / pack-models / pack-launcher
#   ./build.sh clean                        清理构建产物
#   ./build.sh doctor                       dotnet 信息
#   ./build.sh dist                         列出产物
#
# 环境变量：
#   GC_VERBOSE=1     显示细节（各步骤日志尾部、产物路径）
#   GC_QUIET=1       只显示警告与错误
#   GC_NO_PROGRESS=1 关闭进度条（保留步骤行）
#   FLUTTER_BIN      指定 flutter 可执行文件
#
# 实际代码都在 buildsh/ 里，本文件只负责加载模块与分发。
# ---------------------------------------------------------------------------
set -eu

# Avalonia 遥测任务在只读用户目录/CI/沙箱会因写日志失败中断编译（MSB4018）
export AVALONIA_TELEMETRY_OPTOUT=1

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT_DIR"
export GC_ROOT="$ROOT_DIR"

APP_PROJ="GC-BOOT/GC-BOOT.csproj"
SAVEDOG_PROJ="GC-SAVEDOG/GC-SAVEDOG.csproj"
STAMP_PROJ="model/_engine/VersionStamp/VersionStamp.csproj"
SLN="GC.sln"
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props | head -1)
[ -n "$VERSION" ] || VERSION="1.0.0"

TASK="${1:-build}"
shift 2>/dev/null || true

# 解析 -version= / --version= / --targets= / --allow-no-ui
CLI_VERSION=""
for arg in "$@"; do
  case "$arg" in
    -version=*|--version=*) CLI_VERSION="${arg#*=}" ;;
    -targets=*|--targets=*|--only=*) export GC_TARGETS="${arg#*=}" ;;
    -allow-no-ui|--allow-no-ui) export GC_ALLOW_NO_UI=1 ;;
  esac
done
if [ -n "$CLI_VERSION" ]; then
  VERSION="$CLI_VERSION"
  # 写回 Directory.Build.props，让程序集版本与关于页显示一致
  if [ -f Directory.Build.props ]; then
    sed -i.bak "s|<Version>[^<]*</Version>|<Version>${VERSION}</Version>|" Directory.Build.props
    rm -f Directory.Build.props.bak
  fi
  printf '==> 版本号设为 %s\n' "$VERSION"
fi
mkdir -p "$ROOT_DIR/build/logs"

# 加载模块（顺序即依赖顺序）
for _m in log progress publish core flutter package; do
  . "$ROOT_DIR/buildsh/lib/$_m.sh"
done
unset _m
. "$ROOT_DIR/buildsh/build.sh"
. "$ROOT_DIR/buildsh/check.sh"
. "$ROOT_DIR/buildsh/tools.sh"
. "$ROOT_DIR/buildsh/release.sh"

case "$TASK" in
  build)           bs_build "$ROOT_DIR" ;;
  check)           bs_check "$ROOT_DIR" ;;
  release-verify)  bs_verify_release "$ROOT_DIR" "$@" ;;
  *)               bs_tools "$ROOT_DIR" "$TASK" ;;
esac
