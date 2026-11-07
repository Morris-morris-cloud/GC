#!/bin/sh
# ---------------------------------------------------------------------------
# Earth Model Studio 构建脚本（macOS / Linux）
#
#   ./build.sh build          编译（Debug）
#   ./build.sh run            本机运行
#   ./build.sh test           离屏渲染校验，截图输出到 build/screenshots
#   ./build.sh models         编译模型 dll + 生成封面 + 自动打版本
#   ./build.sh stamp          只重新生成 version.json 与模型版本
#   ./build.sh publish        编译三个平台启动器（不内置模型）+ 建立 .GC 运行目录
#   ./build.sh pack-models    单独打包模型数据（含 version.json）
#   ./build.sh pack-launcher  生成三个平台的启动器更新包 zip
#   ./build.sh update-test    离线验证自动更新链路
#   ./build.sh clean          清理
#
# 分层：src/ 只是启动器；模型全部在 model/；**发布包不内置模型**。
#
# 运行目录（发布包里就长这样）：
#   <安装目录>/
#     EarthModelStudio(.exe | .app/...)
#     .GC/
#       settings.json        设置与已安装版本
#       model/               启动时从 gitcode 下载的模型
#       cache/               下载暂存
#       update/              待下次启动应用的启动器更新
# ---------------------------------------------------------------------------
set -eu

# Avalonia 遥测任务在只读用户目录/CI/沙箱会因写日志失败而中断编译（MSB4018）。
# 官方 opt-out；Directory.Build.targets 里还有一层项目内的兜底。
export AVALONIA_TELEMETRY_OPTOUT=1

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT_DIR"

APP_PROJ="src/main/dotnet/EarthModelStudio/EarthModelStudio.csproj"
PREVIEW_PROJ="model/_engine/PreviewGen/PreviewGen.csproj"
STAMP_PROJ="model/_engine/VersionStamp/VersionStamp.csproj"
TEST_PROJ="src/test/dotnet/EarthModelStudio.Tests/EarthModelStudio.Tests.csproj"
SLN="EarthModelStudio.sln"
VERSION="1.0.0"
TASK="${1:-build}"

# 按模型包内容指纹自动写回 <id>.json 的 version，并生成 version.json
stamp_versions() {
  echo "==> 自动计算模型版本并生成 version.json"
  dotnet run --project "$STAMP_PROJ" -c Release -- "$ROOT_DIR" "$VERSION"
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
  stamp_versions
}

# 发布目录只放启动器 + 空的 .GC 运行骨架，绝不放模型
prepare_runtime_layout() {
  REL="$1"
  DEST="$ROOT_DIR/$REL"
  [ -d "$DEST" ] || return 0
  rm -rf "$DEST/model" "$DEST/examples"
  mkdir -p "$DEST/.GC/model" "$DEST/.GC/cache" "$DEST/.GC/update"
  cat > "$DEST/.GC/model/README.txt" <<'TXT'
模型库目录（.GC/model/）
启动时程序会读取 gitcode 仓库的 version.json，把最新版模型下载到这里。
离线使用也可以手动放入 <模型id>/<模型id>.json + <模型id>/<模型id>.model/。

运行目录：
  <可执行文件>
  .GC/settings.json
  .GC/model/      本目录（下载的模型）
  .GC/cache/      下载暂存
  .GC/update/     待下次启动应用的启动器更新
TXT
  if [ ! -f "$DEST/.GC/settings.json" ]; then
    cat > "$DEST/.GC/settings.json" <<'JSON'
{
  "language": "auto",
  "theme": "light",
  "renderFps": 30,
  "memoryMb": 2048,
  "autoUpdateOnStartup": true,
  "updateRepository": "https://gitcode.com/2509_92447106/GC",
  "updateManifestUrl": "",
  "updateLocalRoot": "",
  "installedModelVersions": {},
  "launcherVersion": "",
  "pendingLauncherVersion": "",
  "lastUpdateCheck": "",
  "sources": [],
  "recentModels": []
}
JSON
  fi
  echo "    运行目录 -> $REL/.GC （settings.json + model/ + cache/ + update/）"
}

# 系统/架构名（用于 Release 附件命名：GC-版本-系统-架构-是否内置dotnet）
rid_label() {
  case "$1" in
    win-x64)   echo "Win-Amd64" ;;
    win-arm64) echo "Win-Arm64" ;;
    osx-arm64) echo "Mac-Arm64" ;;
    *)         echo "$1" ;;
  esac
}

# $1=RID $2=输出目录 $3=self-contained(true/false)
publish_rid() {
  RID="$1"; OUT="$2"; SELF="$3"
  echo ""
  echo "==> 编译 ${RID} (self-contained=${SELF}) -> ${OUT}"
  dotnet publish "$APP_PROJ" \
    -c Release \
    -r "$RID" \
    --self-contained "$SELF" \
    -p:Version="$VERSION" \
    -p:DebugType=none \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=false \
    -o "$ROOT_DIR/$OUT"
  rm -rf "$ROOT_DIR/$OUT/model" "$ROOT_DIR/$OUT/examples"
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

# 启动器更新包：只包含程序文件，排除 .GC（否则会覆盖用户已下载的模型与设置）
# 命名：GC-<版本>-<系统>-<架构>-<是否内置dotnet>.zip
#   selfcontained = 内置 .NET 运行时（免安装，体积大）
#   framework     = 不内置（需机器已装 .NET 8，体积小）
# 3 平台 × 2 种 = 6 个发布包
pack_launcher() {
  echo "==> 生成 6 个启动器发布包（不含 .GC）"
  WIN64="$ROOT_DIR/dist/win-x64"
  WINARM="$ROOT_DIR/dist/win-arm64"
  MACBIN="$ROOT_DIR/dist/mac-arm64/EarthModelStudio.app/Contents/MacOS"
  [ -d "$MACBIN" ] || MACBIN="$ROOT_DIR/dist/mac-arm64"
  pack_one "Win-Amd64" "$WIN64"
  pack_one "Win-Arm64" "$WINARM"
  pack_one "Mac-Arm64" "$MACBIN"
  stamp_versions
  ls -lh "$ROOT_DIR"/dist/GC-"$VERSION"-*.zip 2>/dev/null || true
}

# 同一份产物打两个包（内置 / 不内置 dotnet 的区分由 publish 阶段分别输出到
# dist/<rid>-selfcontained 与 dist/<rid>-framework，文件名后缀标识即可）
pack_one() {
  LABEL="$1"; SRC="$2"
  [ -d "$SRC" ] || return 0
  for KIND in selfcontained framework; do
    DIR="${SRC}-${KIND}"
    [ -d "$DIR" ] || DIR="$SRC"
    ZIP="$ROOT_DIR/dist/GC-${VERSION}-${LABEL}-${KIND}.zip"
    ( cd "$DIR" && zip -qr "$ZIP" . -x "./.GC" "./.GC/*" "*.DS_Store" )
    echo "    -> $(basename "$ZIP")"
  done
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
    stamp_versions
    # 3 平台 × {内置dotnet, 不内置dotnet} = 6 个发布包
    for RID in osx-arm64 win-x64 win-arm64; do
      publish_rid "$RID" "dist/$RID-selfcontained" true
      publish_rid "$RID" "dist/$RID-framework" false
    done
    # 兼容原有 .app 结构与 .GC 骨架（取内置版作为代表）
    [ -d "$ROOT_DIR/dist/mac-arm64" ] || cp -R "$ROOT_DIR/dist/osx-arm64-selfcontained" "$ROOT_DIR/dist/mac-arm64"
    [ -d "$ROOT_DIR/dist/win-x64" ] || cp -R "$ROOT_DIR/dist/win-x64-selfcontained" "$ROOT_DIR/dist/win-x64"
    [ -d "$ROOT_DIR/dist/win-arm64" ] || cp -R "$ROOT_DIR/dist/win-arm64-selfcontained" "$ROOT_DIR/dist/win-arm64"
    package_mac_app
    prepare_runtime_layout "dist/win-x64"
    prepare_runtime_layout "dist/win-arm64"
    prepare_runtime_layout "dist/mac-arm64/EarthModelStudio.app/Contents/MacOS"
    pack_launcher
    echo ""
    echo "==> 三平台 × 2 模式 = 6 个发布包完成（不含模型，首次启动自动下载）："
    ls -lh "$ROOT_DIR"/dist/GC-"$VERSION"-*.zip 2>/dev/null || true
    echo "    把 6 个 zip 传 Release，把 version.json / model/ 推送到 gitcode"
    ;;
  models)
    build_models
    ;;
  stamp)
    stamp_versions
    ;;
  bundle)
    echo "已弃用：发布包不再内置模型。请改用 ./build.sh stamp 与 gitcode 自动更新。"
    ;;
  pack-models)
    build_models
    pack_models
    ;;
  pack-launcher)
    pack_launcher
    ;;
  update-test)
    dotnet run --project "$TEST_PROJ" -c Debug -- --update-test "$ROOT_DIR"
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
    echo "可用: build | run | test | publish | models | stamp | pack-models | pack-launcher | update-test | clean | doctor"
    exit 1
    ;;
esac
