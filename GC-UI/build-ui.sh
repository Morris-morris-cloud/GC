#!/usr/bin/env bash
# 构建 GC-UI 前端（macOS / Linux 上运行；Windows 请用 build-ui.cmd）。
#
#   ./GC-UI/build-ui.sh            # 构建当前平台
#   ./GC-UI/build-ui.sh macos      # 指定目标
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FRONTEND="$ROOT/GC-UI/frontend"
TARGET="${1:-}"

if ! command -v flutter >/dev/null 2>&1; then
  echo "找不到 flutter，请先安装 Flutter SDK 并加入 PATH。" >&2
  exit 1
fi

if [ -z "$TARGET" ]; then
  case "$(uname -s)" in
    Darwin) TARGET="macos" ;;
    *)      TARGET="macos" ;;
  esac
fi

echo "==> 构建后端（GC.UI.Server）"
dotnet build "$ROOT/GC-UI/backend/GC.UI.Server/GC.UI.Server.csproj" -c Release

echo "==> 构建前端（$TARGET）"
cd "$FRONTEND"
flutter pub get
flutter build "$TARGET" --release

echo "==> 完成"
case "$TARGET" in
  macos)   echo "产物: $FRONTEND/build/macos/Build/Products/Release/" ;;
  windows) echo "产物: $FRONTEND/build/windows/x64/runner/Release/" ;;
  *)       echo "产物: $FRONTEND/build/$TARGET/" ;;
esac
