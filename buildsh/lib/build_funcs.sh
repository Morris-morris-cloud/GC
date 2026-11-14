#!/bin/sh
# buildsh/lib/build_funcs.sh —— 构建函数库（由入口脚本 source）
# 依赖调用方提供：ROOT_DIR, APP_PROJ, PREVIEW_PROJ, STAMP_PROJ, TEST_PROJ, SLN, VERSION
# 以及任务进度上报函数 step / progress（由进度条引擎提供，未定义时降级为 echo）

if ! type step >/dev/null 2>&1; then step() { echo "==> $1"; }; fi
if ! type progress >/dev/null 2>&1; then progress() { :; }; fi

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
        [ "$NAME" = "GC.ModelSdk.dll" ] && continue
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

  # --- macOS：先得到真实 .app 目录（改名 + 同步 Info.plist）---
  MACAPP="$ROOT_DIR/dist/mac-arm64/GC.app"
  if [ -d "$MACAPP" ]; then
    FINAL="$ROOT_DIR/dist/GC-${VERSION}-mac-arm64.app"
    rm -rf "$FINAL"
    mv "$MACAPP" "$FINAL"
    chmod +x "$FINAL/Contents/MacOS/GC" 2>/dev/null || true
    PLIST="$FINAL/Contents/Info.plist"
    if [ -f "$PLIST" ]; then
      /usr/libexec/PlistBuddy -c "Set :CFBundleName Morris GC" "$PLIST" 2>/dev/null || true
      /usr/libexec/PlistBuddy -c "Set :CFBundleDisplayName Morris GC" "$PLIST" 2>/dev/null || true
    fi
    xattr -cr "$FINAL" 2>/dev/null || true
    echo "    -> $(basename "$FINAL")/"
  fi

  # --- 统一压缩：每个产物都打成同名 .zip ---
  #   GC-1.0.3-win-amd64.exe  -> GC-1.0.3-win-amd64.zip
  #   GC-1.0.3-win-arm64.exe  -> GC-1.0.3-win-arm64.zip
  #   GC-1.0.3-mac-arm64.app  -> GC-1.0.3-mac-arm64.zip
  # 客户端下载时按「原名去掉扩展名 + .zip」取包，再解压回原名。
  echo "==> 压缩为 zip"
  for art in "$ROOT_DIR"/dist/GC-"$VERSION"-*.exe "$ROOT_DIR"/dist/GC-"$VERSION"-*.app; do
    [ -e "$art" ] || continue
    base=$(basename "$art")
    name="${base%.*}"          # 去掉 .exe / .app
    ZIP="$ROOT_DIR/dist/${name}.zip"
    rm -f "$ZIP"
    if [ -d "$art" ]; then
      # 目录（.app）：保留权限与符号链接
      if command -v ditto >/dev/null 2>&1; then
        ditto -c -k --sequesterRsrc --keepParent "$art" "$ZIP"
      else
        ( cd "$(dirname "$art")" && zip -qry "$ZIP" "$base" -x "*.DS_Store" )
      fi
    else
      ( cd "$(dirname "$art")" && zip -q "$ZIP" "$base" )
    fi
    echo "    -> ${name}.zip"
  done

  # 原始产物（.exe / .app 目录）不再保留：只发布 zip
  rm -f "$ROOT_DIR"/dist/GC-"$VERSION"-*.exe
  rm -rf "$ROOT_DIR"/dist/GC-"$VERSION"-*.app

  strip_intermediate_dirs
  stamp_versions
  echo ""
  echo "==> dist 只保留可发布的 zip："
  ls -lh "$ROOT_DIR"/dist/GC-"$VERSION"-*.zip 2>/dev/null || true
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
  APP="$DIST/GC.app"
  rm -rf "$APP"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>GC</string>
  <key>CFBundleDisplayName</key><string>GC</string>
  <key>CFBundleIdentifier</key><string>cn.neorf.GC</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>GC</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
  for f in "$DIST"/*; do
    base=$(basename "$f")
    [ "$base" = "GC.app" ] && continue
    [ "$base" = ".DS_Store" ] && continue
    mv "$f" "$APP/Contents/MacOS/"
  done
  # 应用图标：由 Assets/icon-1024.png 生成 .icns
  ICONSRC="$ROOT_DIR/GC-BOOT/Assets/icon-1024.png"
  if [ -f "$ICONSRC" ] && command -v iconutil >/dev/null 2>&1; then
    ICONSET="$DIST/AppIcon.iconset"
    rm -rf "$ICONSET"; mkdir -p "$ICONSET"
    for sz in 16 32 64 128 256 512; do
      sips -z $sz $sz "$ICONSRC" --out "$ICONSET/icon_${sz}x${sz}.png" >/dev/null 2>&1
      d=$((sz * 2))
      sips -z $d $d "$ICONSRC" --out "$ICONSET/icon_${sz}x${sz}@2x.png" >/dev/null 2>&1
    done
    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns" 2>/dev/null && \
      echo "    + AppIcon.icns"
    rm -rf "$ICONSET"
    # Info.plist 增加图标项
    if [ -f "$APP/Contents/Info.plist" ]; then
      /usr/libexec/PlistBuddy -c "Delete :CFBundleIconFile" "$APP/Contents/Info.plist" 2>/dev/null || true
      /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$APP/Contents/Info.plist" 2>/dev/null || true
    fi
  fi

  # 发布包不带 .GC 运行骨架（用户首次启动自行创建），避免覆盖用户数据
  rm -rf "$APP/Contents/MacOS/.GC"
  chmod +x "$APP/Contents/MacOS/GC"
  echo "==> 已打包 $APP"
}

