#!/bin/sh
# ---------------------------------------------------------------------------
# GC 构建入口（薄封装）
#
#   ./build.sh build       完整发布（原 publish），带 apt 风格进度条
#                          默认要求每个平台都带 Flutter 前端；--allow-no-ui 允许降级
#   ./build.sh check       验证：编译 + 自检 + 离屏渲染 + 产物
#   ./build.sh models      编译模型 dll + 封面 + 版本戳
#   ./build.sh stamp       重新生成 version.json
#   ./build.sh run / test / update-test / pack-models / pack-launcher
#   ./build.sh clean       清理
#   ./build.sh doctor      dotnet 信息
#   ./build.sh dist        列出产物
#
# 实际代码都在 buildsh/ 里，本文件只负责加载模块与分发。
# ---------------------------------------------------------------------------
set -eu

# Avalonia 遥测任务在只读用户目录/CI/沙箱会因写日志失败中断编译（MSB4018）
export AVALONIA_TELEMETRY_OPTOUT=1

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT_DIR"

APP_PROJ="GC-BOOT/GC-BOOT.csproj"
SAVEDOG_PROJ="GC-SAVEDOG/GC-SAVEDOG.csproj"
PREVIEW_PROJ="model/_engine/PreviewGen/PreviewGen.csproj"
STAMP_PROJ="model/_engine/VersionStamp/VersionStamp.csproj"
TEST_PROJ="GC/src/test/dotnet/GC.Tests/GC.Tests.csproj"
SLN="GC.sln"
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props | head -1)
[ -n "$VERSION" ] || VERSION="1.0.0"

TASK="${1:-build}"
shift 2>/dev/null || true

# 解析 -version=<版本号>（也接受 --version=）与 --allow-no-ui
CLI_VERSION=""
for arg in "$@"; do
  case "$arg" in
    -version=*|--version=*) CLI_VERSION="${arg#*=}" ;;
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
  echo "==> 版本号设为 ${VERSION}"
fi
mkdir -p "$ROOT_DIR/build/logs"

# 加载 buildsh 模块
. "$ROOT_DIR/buildsh/lib/progress.sh"
. "$ROOT_DIR/buildsh/lib/build_funcs.sh"
. "$ROOT_DIR/buildsh/build.sh"
. "$ROOT_DIR/buildsh/check.sh"
. "$ROOT_DIR/buildsh/tools.sh"

case "$TASK" in
  build)        bs_build "$ROOT_DIR" ;;
  check)        bs_check "$ROOT_DIR" ;;
  *)            bs_tools "$ROOT_DIR" "$TASK" ;;
esac
