#!/bin/sh
# ---------------------------------------------------------------------------
# Earth Model Studio 构建脚本（macOS / Linux）
#
#   ./build.sh build      编译（Debug）
#   ./build.sh run        本机运行
#   ./build.sh test       离屏渲染校验，截图输出到 build/screenshots
#   ./build.sh publish    一次性编译三个平台 -> dist/
#   ./build.sh models     编译模型功能程序集 + 生成模型封面（写进 model/<id>/<id>.model）
#   ./build.sh pack-models 编译模型后把 model/ 整体打包 -> dist/models-x.y.z.zip
#   ./build.sh clean      清理
#
# 分层约定：src/ 只是启动器，不含任何模型数据；模型全部放在 model/（外置数据）。
#
# publish 产出（启动器，模型库目录为空）：
#   dist/mac-arm64/EarthModelStudio.app     macOS Apple Silicon
#   dist/win-x64/EarthModelStudio.exe       Windows x86_64
#   dist/win-arm64/EarthModelStudio.exe     Windows on ARM
# ---------------------------------------------------------------------------
set -e

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT_DIR"

APP_PROJ="src/main/dotnet/EarthModelStudio/EarthModelStudio.csproj"
PREVIEW_PROJ="model/_engine/PreviewGen/PreviewGen.csproj"
TEST_PROJ="src/test/dotnet/EarthModelStudio.Tests/EarthModelStudio.Tests.csproj"
SLN="EarthModelStudio.sln"
VERSION="1.0.0"
TASK="${1:-build}"

publish_rid() {
  RID="$1"; OUT="$2"
  echo ""
  echo "==> 编译 ${RID} -> ${OUT}"
  dotnet publish "$APP_PROJ" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:Version="$VERSION" \
    -p:DebugType=none \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=false \
    -o "$ROOT_DIR/$OUT"
  # 启动器不带模型：只留一个空模型库目录，模型作为独立内容放置 / 下载
  rm -rf "$ROOT_DIR/$OUT/model" "$ROOT_DIR/$OUT/examples"
  mkdir -p "$ROOT_DIR/$OUT/model"
  cp "$ROOT_DIR/model/README.md" "$ROOT_DIR/$OUT/model/README.md" 2>/dev/null || true
}

# 编译模型自己的功能程序集，并把 dll / 封面直接写进模型包
build_models() {
  echo "==> 编译模型功能程序集（源码就在各模型目录的 src/ 里）"
  for DIR in "$ROOT_DIR"/model/*/; do
    ID=$(basename "$DIR")
    [ -d "$DIR/src" ] || continue
    PROJ=$(ls "$DIR"src/*.csproj 2>/dev/null | head -1)
    [ -n "$PROJ" ] || continue
    dotnet build "$PROJ" -c Release
    if [ -d "$DIR$ID.model" ]; then
      for DLL in "$DIR"src/bin/Release/net8.0/*.dll; do
        [ -f "$DLL" ] || continue
        NAME=$(basename "$DLL")
        [ "$NAME" = "EarthModelStudio.ModelSdk.dll" ] && continue
        cp "$DLL" "$DIR$ID.model/"
        echo "    -> model/$ID/$ID.model/$NAME"
      done
    fi
  done
  echo "==> 用模型自己的渲染器生成封面"
  dotnet run --project "$PREVIEW_PROJ" -c Release -- "$ROOT_DIR"
}

# 把 model/ 里的模型数据单独打包（与启动器分开分发）
pack_models() {
  mkdir -p "$ROOT_DIR/dist"
  ZIP="$ROOT_DIR/dist/models-$VERSION.zip"
  rm -f "$ZIP"
  echo "==> 打包模型数据 -> dist/models-$VERSION.zip"
  ( cd "$ROOT_DIR" && zip -qr "$ZIP" model \
      -x "*.DS_Store" "*/bin/*" "*/obj/*" "*.user" )
  du -sh "$ZIP"
}

package_mac_app() {
  DIST="$ROOT_DIR/dist/mac-arm64"
  APP="$DIST/EarthModelStudio.app"
  rm -rf "$APP"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Earth Model Studio</string>
  <key>CFBundleDisplayName</key><string>Earth Model Studio</string>
  <key>CFBundleIdentifier</key><string>cn.neorf.earthmodelstudio</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>EarthModelStudio</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
  for f in "$DIST"/*; do
    base=$(basename "$f")
    [ "$base" = "EarthModelStudio.app" ] && continue
    mv "$f" "$APP/Contents/MacOS/"
  done
  chmod +x "$APP/Contents/MacOS/EarthModelStudio"
  echo "==> 已打包 $APP"
}

case "$TASK" in
  build)
    dotnet build "$SLN" -c Debug
    ;;
  run)
    dotnet run --project "$APP_PROJ" -c Debug
    ;;
  test)
    dotnet run --project "$TEST_PROJ" -c Debug -- "$ROOT_DIR/build/screenshots"
    ;;
  publish)
    mkdir -p "$ROOT_DIR/dist"
    publish_rid "osx-arm64" "dist/mac-arm64"
    publish_rid "win-x64"   "dist/win-x64"
    publish_rid "win-arm64" "dist/win-arm64"
    package_mac_app
    echo ""
    echo "==> 三个平台编译完成（启动器不含模型数据）："
    du -sh "$ROOT_DIR/dist"/* 2>/dev/null || true
    echo "    模型数据用 ./build.sh pack-models 单独打包，或放进 <安装目录>/model 后再启动"
    ;;
  models)
    build_models
    ;;
  pack-models)
    build_models
    pack_models
    ;;
  clean)
    rm -rf "$ROOT_DIR/build" "$ROOT_DIR/dist"
    find "$ROOT_DIR/src" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
    echo "==> 已清理"
    ;;
  doctor)
    dotnet --info | head -20
    ;;
  *)
    echo "未知任务: $TASK"
    echo "可用: build | run | test | publish | models | pack-models | clean | doctor"
    exit 1
    ;;
esac
