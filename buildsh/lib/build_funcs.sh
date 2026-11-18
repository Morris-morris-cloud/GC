#!/bin/sh
# buildsh/lib/build_funcs.sh —— 构建函数库（由入口脚本 source）
# 依赖调用方提供：ROOT_DIR, APP_PROJ, PREVIEW_PROJ, STAMP_PROJ, TEST_PROJ, SLN, VERSION
# 以及任务进度上报函数 step / progress（由进度条引擎提供，未定义时降级为 echo）

if ! type step >/dev/null 2>&1; then step() { echo "==> $1"; }; fi
if ! type progress >/dev/null 2>&1; then progress() { :; }; fi

stamp_versions() {
  echo "==> 自动计算模型版本并生成 version.json"
  dotnet run --project "$STAMP_PROJ" -c Release -- "$ROOT_DIR" "$VERSION"
  sign_manifest
}

# 打完 zip 之后刷新 launcher.packages（写入 path / dir / name / sha256 / size）。
# 只重写清单的启动器段：模型指纹可能涉及几百 MB 文件，没必要再算一遍。
stamp_launcher_only() {
  echo "==> 刷新启动器包信息（sha256 / 体积 / dist 路径）"
  dotnet run --project "$STAMP_PROJ" -c Release -- "$ROOT_DIR" "$VERSION" --launcher-only
  sign_manifest
}

# 用 .signing/ 里的私钥给 version.json 签名（私钥不入库）。
# 没有私钥时只警告：清单不带 signature，客户端会按「未签名」处理。
sign_manifest() {
  KEY="$ROOT_DIR/.signing/gc-signing.pem"
  KEYID="${GC_SIGNING_KEY_ID:-gc-2026-12}"
  if [ ! -f "$KEY" ]; then
    echo "!! 未找到签名私钥 ${KEY}，本次 version.json 未签名（客户端会拒绝更新）"
    echo "   生成密钥：dotnet run --project GC-SAVEDOG -- keygen"
    return 0
  fi
  echo "==> 签名 version.json（${KEYID}）"
  if dotnet run --project "$ROOT_DIR/GC-SAVEDOG/GC-SAVEDOG.csproj" -c Release -- \
       sign --manifest "$ROOT_DIR/version.json" --key "$KEY" --key-id "$KEYID"; then
    # model/version.json 必须与根清单完全一致（同一个签名），否则走 model 地址的源会验签失败
    cp "$ROOT_DIR/version.json" "$ROOT_DIR/model/version.json"
  else
    echo "!! 签名失败，version.json 保持未签名状态"
    return 1
  fi
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
  mkdir -p "$DEST/.GC/model" "$DEST/.GC/cache" "$DEST/.GC/update" "$DEST/.GC/config"
  cat > "$DEST/.GC/model/README.txt" <<'TXT'
模型库目录（.GC/model/）
启动时程序会读取 GitCode 仓库的 version.json，把最新版模型下载到这里。
离线使用也可以手动放入 <模型id>/<模型id>.json + <模型id>/<模型id>.model/。

运行目录：
  <可执行文件>
  GC-SAVEDOG      更新守护进程（验签 + 原子替换 + 失败回滚）
  .GC/config/settings.json   用户设置（删 .GC/cache 不会丢配置）
  .GC/model/      本目录（下载的模型）
  .GC/cache/      下载暂存
  .GC/update/     待下次启动应用的启动器更新
TXT
  if [ ! -f "$DEST/.GC/config/settings.json" ]; then
    cat > "$DEST/.GC/config/settings.json" <<'JSON'
{
  "language": "auto",
  "theme": "light",
  "renderFps": 30,
  "memoryMb": 2048,
  "autoUpdateOnStartup": true,
  "requireSignedManifest": true,
  "updateRepository": "https://gitcode.com/Morris_1/GC",
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
  echo "    运行目录 -> $REL/.GC （config/settings.json + model/ + cache/ + update/）"
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

  # 更新守护进程：与主程序同目录（macOS 打包时会被一起移入 Contents/MacOS）
  if [ -n "${SAVEDOG_PROJ:-}" ] && [ -f "$ROOT_DIR/$SAVEDOG_PROJ" ]; then
    echo "==> 编译 GC-SAVEDOG (${RID}) -> ${OUT}（裁剪单文件）"
    # 守护进程不引用 Avalonia，可以安全裁剪：70MB -> 14MB。
    # 前提是 JSON 全部走源生成上下文（GC-SAVEDOG/SavedogJson.cs），新增 JSON 类型
    # 忘了注册只会在发布产物里炸，所以 update-test 会跑一次真实 apply 兜底。
    dotnet publish "$SAVEDOG_PROJ" \
      -c Release \
      -r "$RID" \
      --self-contained true \
      -p:Version="$VERSION" \
      -p:DebugType=none \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:PublishTrimmed=true \
      -p:TrimMode=full \
      -p:JsonSerializerIsReflectionEnabledByDefault=false \
      -o "$ROOT_DIR/$OUT"
  fi

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
#
# 产物落点遵循「仓库 dist/<系统>-<架构>/」布局：
#   dist/mac-arm64/GC-<版本>-mac-arm64.zip
#   dist/win-amd64/GC-<版本>-win-amd64.zip
#   dist/win-arm64/GC-<版本>-win-arm64.zip
# 启动器更新时既可以从 Release 附件下载，也会到这些目录里找编译产物
# （见 GC-DOWNLOAD/LauncherArtifacts.cs），所以两种发布方式都能用。
#
# Windows: 单文件 exe 直接改名（本身就是可执行产物）
# macOS:   .app 必须打成 zip 才能作为单文件分发；文件名用 .app.zip，
#          这样 macOS 双击会用归档工具解压出可运行的 .app。
#          若直接命名 .app，系统会把 zip 当成应用包解析 => 报「已损坏/打不开」。
# 发布本地 UI 后端（GC.UI.Server）。启动器会拉起它并等待 /api/health，
# 模型窗口也由它承载，所以必须和启动器放在同一目录。
publish_backend() {
  RID="$1"; OUT="$2"
  echo "==> 编译 UI 后端 (${RID}) -> ${OUT}"
  dotnet publish "$ROOT_DIR/GC-UI/backend/GC.UI.Server/GC.UI.Server.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:Version="$VERSION" \
    -p:DebugType=none \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -o "$ROOT_DIR/$OUT" || return 1
}

# 当前主机原生构建 Flutter 前端（Windows 主机编 Windows，macOS 主机编 macOS）。
# macOS 版 Flutter 桌面只能在 macOS 上编；Windows 版则由 build_flutter_ui_cross
# 在 Linux/macOS 上交叉编译。
build_flutter_ui() {
  PLAT="$1"; DEST="$2"
  UI_SRC="$ROOT_DIR/GC-UI/frontend"
  [ -d "$UI_SRC" ] || return 0

  FLUTTER_BIN="${FLUTTER_BIN:-flutter}"
  if ! command -v "$FLUTTER_BIN" >/dev/null 2>&1; then
    echo "!! 未找到 flutter（可用 FLUTTER_BIN=<路径> 指定），本次包内不含 Flutter 界面"
    return 0
  fi

  echo "==> 构建 Flutter 前端 (${PLAT})"
  ( cd "$UI_SRC" && "$FLUTTER_BIN" pub get && "$FLUTTER_BIN" build "$PLAT" --release ) || return 1

  mkdir -p "$ROOT_DIR/$DEST"
  case "$PLAT" in
    windows)
      # Release/ 里含 GC-UI.exe、flutter_windows.dll 和 data/
      cp -R "$UI_SRC/build/windows/x64/runner/Release/." "$ROOT_DIR/$DEST/"
      ;;
    macos)
      rm -rf "$ROOT_DIR/$DEST/GC-UI.app"
      cp -R "$UI_SRC/build/macos/Build/Products/Release/GC-UI.app" "$ROOT_DIR/$DEST/"
      ;;
  esac
  echo "    -> $DEST（Flutter ${PLAT}）"
}

# 在 Linux / macOS 上交叉编译 Windows 前端：mingw-w64 编 C++ 外壳，
# wine 跑 Windows 版 gen_snapshot 生成 AOT 快照，无需 Windows 机器。
# 产物布局与官方 toolchain 完全一致（GC-UI.exe + flutter_windows.dll + data/）。
build_flutter_ui_cross() {
  DEST="$1"
  UI_SRC="$ROOT_DIR/GC-UI/frontend"
  [ -d "$UI_SRC" ] || return 0

  FLUTTER_BIN="${FLUTTER_BIN:-flutter}"
  if ! command -v "$FLUTTER_BIN" >/dev/null 2>&1; then
    echo "!! 未找到 flutter（可用 FLUTTER_BIN=<路径> 指定），本次包内不含 Flutter 界面"
    return 0
  fi

  mkdir -p "$ROOT_DIR/build/logs"
  echo "==> 交叉编译 Windows 前端（mingw-w64 + wine）"
  if ! FLUTTER_BIN="$FLUTTER_BIN" bash "$ROOT_DIR/buildsh/cross/build-ui-win-cross.sh" \
       "$UI_SRC/build/windows/x64/runner/Release" \
       >"$ROOT_DIR/build/logs/flutter-win-cross.log" 2>&1; then
    echo "!! 交叉编译失败，本次包内不含 Flutter 界面（日志：build/logs/flutter-win-cross.log）"
    return 0
  fi

  mkdir -p "$ROOT_DIR/$DEST"
  cp -R "$UI_SRC/build/windows/x64/runner/Release/." "$ROOT_DIR/$DEST/"
  echo "    -> $DEST（Flutter windows，交叉编译）"
}

pack_launcher() {
  echo "==> 生成启动器产物（GC-<版本>-<系统>-<架构>）"

  # --- Windows：启动器 + 守护进程 + UI 后端 + Flutter 前端，整目录打包 ---
  for pair in "win-x64:win-amd64" "win-arm64:win-arm64"; do
    RID="${pair%%:*}"; LABEL="${pair##*:}"
    SRC="$ROOT_DIR/dist/$LABEL"
    [ -d "$SRC" ] || SRC="$ROOT_DIR/dist/$RID"      # 兼容旧布局
    [ -d "$SRC" ] || continue

    # 启动器固定是 GC.exe；后端与前端是可选的随包产物，不能当成启动器。
    EXE="$SRC/GC.exe"
    [ -f "$EXE" ] || EXE=$(ls "$SRC"/*.exe 2>/dev/null | grep -v 'GC-SAVEDOG\|GC.UI.Server\|GC-UI' | head -1)
    [ -n "$EXE" ] || continue

    TMP="$ROOT_DIR/dist/.winpkg-$LABEL"
    rm -rf "$TMP"; mkdir -p "$TMP"
    cp -R "$SRC"/. "$TMP"/
    rm -f "$TMP"/*.zip
    mv "$TMP/$(basename "$EXE")" "$TMP/GC-${VERSION}-${LABEL}.exe"
    chmod +x "$TMP/GC-${VERSION}-${LABEL}.exe" 2>/dev/null || true
    if [ ! -f "$TMP/GC-SAVEDOG.exe" ]; then
      echo "    !! 缺少 GC-SAVEDOG.exe，该平台将退回旧脚本更新"
    fi
    if [ ! -f "$TMP/GC-UI.exe" ]; then
      echo "    !! 包内没有 Flutter 前端，启动器将使用内置 Avalonia 界面"
    fi

    ZIP="$ROOT_DIR/dist/$LABEL/GC-${VERSION}-${LABEL}.zip"
    rm -f "$ZIP"
    ( cd "$TMP" && zip -qr "$ZIP" . )
    rm -rf "$TMP"
    echo "    -> dist/$LABEL/GC-${VERSION}-${LABEL}.zip"
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

  # --- 统一压缩：每个产物都打成同名 .zip，放进自己的平台目录 ---
  #   GC-1.0.3-mac-arm64.app  -> dist/mac-arm64/GC-1.0.3-mac-arm64.zip
  # 客户端下载时按「原名去掉扩展名 + .zip」取包，再解压回原名。
  echo "==> 压缩为 zip"
  for art in "$ROOT_DIR"/dist/GC-"$VERSION"-*.exe "$ROOT_DIR"/dist/GC-"$VERSION"-*.app; do
    [ -e "$art" ] || continue
    base=$(basename "$art")
    name="${base%.*}"          # 去掉 .exe / .app
    label="${name#GC-${VERSION}-}"   # mac-arm64 / win-amd64
    ZIP="$ROOT_DIR/dist/${label}/${name}.zip"
    mkdir -p "$ROOT_DIR/dist/${label}"
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
    echo "    -> dist/${label}/${name}.zip"
  done

  # 原始产物（.exe / .app 目录）不再保留：只发布 zip。
  # 用「移入暂存区」而不是 rm -rf：.app 里有两百多个文件，
  # 一次性删除又慢又不可逆，先挪走更安全（暂存区在下次构建开始时挪出 dist）。
  TRASH="$ROOT_DIR/build/trash"
  mkdir -p "$TRASH"
  for leftover in "$ROOT_DIR"/dist/GC-"$VERSION"-*.exe "$ROOT_DIR"/dist/GC-"$VERSION"-*.app; do
    [ -e "$leftover" ] || continue
    mv "$leftover" "$TRASH/$(basename "$leftover")-$(date +%s)" 2>/dev/null || true
  done

  strip_intermediate_dirs
  stamp_launcher_only
  echo ""
  echo "==> dist 里可发布的 zip："
  ls -lh "$ROOT_DIR"/dist/*/GC-"$VERSION"-*.zip 2>/dev/null || true
}

# 删除 publish 过程中的中间目录：平台目录里只留最终 zip
strip_intermediate_dirs() {
  for d in win-amd64 win-x64 win-arm64 mac-arm64; do
    [ -d "$ROOT_DIR/dist/$d" ] || continue
    find "$ROOT_DIR/dist/$d" -mindepth 1 -maxdepth 1 \
      ! -name "GC-${VERSION}-*.zip" -exec rm -rf {} + 2>/dev/null || true
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
  <key>CFBundleIdentifier</key><string>com.xhmcl.GC</string>
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
  ICONSRC="$ROOT_DIR/GC/src/Assets/icon-1024.png"
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

