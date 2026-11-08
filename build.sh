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
VERSION="1.0.2"
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

# $1=RID $2=输出目录 $3=singlefile(true/false)
publish_rid() {
  RID="$1"; OUT="$2"; SINGLE="${3:-true}"
  echo ""
  echo "==> 编译 ${RID} (single-file=${SINGLE}) -> ${OUT}"
  # 先删旧产物，避免上次 publish 的残留文件混进新包
  rm -rf "$ROOT_DIR/$OUT"
  dotnet publish "$APP_PROJ" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:Version="$VERSION" \
    -p:DebugType=none \
    -p:PublishSingleFile="$SINGLE" \
    -p:IncludeNativeLibrariesForSelfExtract="$SINGLE" \
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
# 产物命名：GC-<版本>-<系统>-<架构>.exe / GC-<版本>-<系统>-<架构>.app.zip
# Windows: 单文件 exe 直接改名（本身就是可执行产物）
# macOS:   .app 必须打成 zip 才能作为单文件分发；文件名用 .app.zip，
#          这样 macOS 双击会用归档工具解压出可运行的 .app。
#          若直接命名 .app，系统会把 zip 当成应用包解析 => 报「已损坏/打不开」。
pack_launcher() {
  echo "==> 生成启动器产物（GC-<版本>-<系统>-<架构>）"

  # --- Windows：exe 单文件 ---
  for pair in "win-x64:win-amd64" "win-arm64:win-arm64"; do
    RID="${pair%%:*}"; LABEL="${pair##*:}"
    SRC="$ROOT_DIR/dist/$RID"
    [ -d "$SRC" ] || continue
    EXE=$(ls "$SRC"/*.exe 2>/dev/null | head -1)
    [ -n "$EXE" ] || continue
    DEST="$ROOT_DIR/dist/GC-${VERSION}-${LABEL}.exe"
    cp "$EXE" "$DEST"
    chmod +x "$DEST"
    echo "    -> $(basename "$DEST")"
  done

  # --- macOS：打包成 zip -> 解压回来 -> 改名 -> 删除 zip ---
  # 这样 dist 里得到的是一个可直接双击运行的真实 .app 目录，
  # 而 zip 只是保证权限/符号链接不丢的中间载体，用完即删。
  MACAPP="$ROOT_DIR/dist/mac-arm64/EarthModelStudio.app"
  if [ -d "$MACAPP" ]; then
    FINAL="$ROOT_DIR/dist/GC-${VERSION}-mac-arm64.app"
    TMPZIP="$ROOT_DIR/dist/.gc-mac-tmp.zip"
    WORK="$ROOT_DIR/dist/.gc-mac-work"

    rm -rf "$FINAL" "$WORK" "$TMPZIP"
    mkdir -p "$WORK"

    # 1) 打包（ditto 保留可执行权限与符号链接，比 zip -y 可靠）
    if command -v ditto >/dev/null 2>&1; then
      ditto -c -k --sequesterRsrc --keepParent "$MACAPP" "$TMPZIP"
    else
      ( cd "$(dirname "$MACAPP")" && zip -qry "$TMPZIP" "$(basename "$MACAPP")" -x "*.DS_Store" )
    fi

    # 2) 解压到工作目录
    if command -v ditto >/dev/null 2>&1; then
      ditto -x -k "$TMPZIP" "$WORK"
    else
      ( cd "$WORK" && unzip -q "$TMPZIP" )
    fi

    # 3) 改名为发布名（保持 .app 是目录）
    SRCAPP="$WORK/EarthModelStudio.app"
    if [ -d "$SRCAPP" ]; then
      mv "$SRCAPP" "$FINAL"
      chmod +x "$FINAL/Contents/MacOS/EarthModelStudio" 2>/dev/null || true
      # 同步 Info.plist 的显示名，避免 Finder 里显示旧名
      PLIST="$FINAL/Contents/Info.plist"
      if [ -f "$PLIST" ]; then
        /usr/libexec/PlistBuddy -c "Set :CFBundleName Morris GC" "$PLIST" 2>/dev/null || true
        /usr/libexec/PlistBuddy -c "Set :CFBundleDisplayName Morris GC" "$PLIST" 2>/dev/null || true
      fi
      # 去掉隔离属性，避免 Gatekeeper 误拦（本地构建产物）
      xattr -cr "$FINAL" 2>/dev/null || true
      echo "    -> $(basename "$FINAL")/  (目录)"
    else
      echo "    !! macOS 打包失败：解压后未找到 EarthModelStudio.app" >&2
    fi

    # 4) 删除 zip 与工作目录
    rm -rf "$WORK" "$TMPZIP"
  fi

  strip_intermediate_dirs
  stamp_versions
  echo ""
  echo "==> dist 只保留可发布的附件："
  ls -lh "$ROOT_DIR"/dist/GC-"$VERSION"-* 2>/dev/null || true
}

# 删除 publish 过程中的中间目录，dist 里只留最终附件
strip_intermediate_dirs() {
  for d in win-x64 win-arm64 mac-arm64; do
    [ -d "$ROOT_DIR/dist/$d" ] && rm -rf "$ROOT_DIR/dist/$d"
  done
  find "$ROOT_DIR/dist" -name ".DS_Store" -delete 2>/dev/null || true
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
    [ "$base" = ".DS_Store" ] && continue
    mv "$f" "$APP/Contents/MacOS/"
  done
  # 发布包不带 .GC 运行骨架（用户首次启动自行创建），避免覆盖用户数据
  rm -rf "$APP/Contents/MacOS/.GC"
  chmod +x "$APP/Contents/MacOS/EarthModelStudio"
  echo "==> 已打包 $APP"
}

case "$TASK" in
  build)
    # 清理上次遗留的发布包 zip，避免误用旧产物
    rm -f "$ROOT_DIR"/dist/GC-*.zip "$ROOT_DIR"/dist/models-*.zip 2>/dev/null || true
    dotnet build "$SLN" -c Debug
    ;;
  run)
    dotnet run --project "$APP_PROJ" -c Debug
    ;;
  test)
    dotnet run --project "$TEST_PROJ" -c Debug -- "$ROOT_DIR/build/screenshots"
    ;;
  publish)
    # 先清掉 dist 旧产物（含历史 zip / 目录），再重新生成
    echo "==> 清理 dist 旧产物"
    rm -rf "$ROOT_DIR/dist"
    mkdir -p "$ROOT_DIR/dist"
    stamp_versions
    # macOS 用目录形式（非单文件）：单文件包启动时需要解压缓存目录，
    # 在受限/沙箱环境下创建失败会直接报「打不开」。目录形式无此依赖。
    publish_rid "osx-arm64" "dist/mac-arm64" false
    publish_rid "win-x64"   "dist/win-x64"   true
    publish_rid "win-arm64" "dist/win-arm64" true
    package_mac_app
    prepare_runtime_layout "dist/win-x64"
    prepare_runtime_layout "dist/win-arm64"
    prepare_runtime_layout "dist/mac-arm64/EarthModelStudio.app/Contents/MacOS"
    pack_launcher
    echo ""
    echo "==> 三个平台编译完成（发布包不含模型，首次启动自动下载）："
    du -sh "$ROOT_DIR/dist"/* 2>/dev/null || true
    echo "    把 version.json / model/ 推送到 gitcode，启动器就会自动检查更新"
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
