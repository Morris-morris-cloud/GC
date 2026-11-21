#!/bin/sh
# buildsh/lib/package.sh —— 组装 .app / 生成发布 zip
#
# 打包策略（相对旧实现的收益）：
#   旧：publish 到 dist/<平台> → cp -R 整个目录到暂存区 → zip → rm -rf 两处
#       每个平台白白多搬一份 200MB，还要删两次上万文件。
#   新：publish 到 build/stage/<平台> → 就地改名/裁剪 → 直接从 stage 压缩
#       → 目录挪进 trash。零冗余复制，删除也不再是批量递归删。

# 剔除与目标平台无关 / 只在调试时才加载的组件。
# Windows 用 Kestrel 自托管，不经 IIS/HttpSys；Avalonia 在 Windows 上只走 Win32 后端；
# DAC/DBI/DiaSymReader 只在调试器与读 PDB 时需要，而发布包 DebugType=none、不带 PDB。
prune_package() {
  _dir="$1"; _plat="$2"
  [ -d "$_dir" ] || return 0
  rm -f "$_dir"/mscordaccore*.dll \
        "$_dir"/mscordbi.dll \
        "$_dir"/Microsoft.DiaSymReader.Native*.dll \
        "$_dir"/web.config \
        "$_dir"/aspnetcorev2_inprocess.dll \
        "$_dir"/Microsoft.AspNetCore.Server.IIS.dll \
        "$_dir"/Microsoft.AspNetCore.Server.IISIntegration.dll \
        "$_dir"/Microsoft.AspNetCore.Server.HttpSys.dll
  case "$_plat" in
    win)
      # ProjectReference 会把后端 exe 的 runtimeconfig 一起拷过来；后端已编入启动器
      rm -f "$_dir"/GC.UI.Server.exe "$_dir"/GC.UI.Server.dll \
            "$_dir"/GC.UI.Server.deps.json "$_dir"/GC.UI.Server.runtimeconfig.json
      rm -f "$_dir"/Avalonia.X11.dll "$_dir"/Avalonia.Native.dll \
            "$_dir"/Avalonia.FreeDesktop.dll "$_dir"/Tmds.DBus.Protocol.dll
      ;;
    linux)
      rm -f "$_dir"/Avalonia.Win32.dll "$_dir"/Avalonia.Win32.Automation.dll
      ;;
  esac
}

# 发布目录只放程序本体：.GC 与 model 都由首次启动自行创建（AppPaths.EnsureCreated）
prepare_runtime_layout() {
  _dir="$1"
  [ -d "$_dir" ] || return 0
  rm -rf "$_dir/model" "$_dir/examples" "$_dir/.GC"
}

_zip_dir() {   # <父目录> <目录名> <zip>
  if [ "$(uname -s)" = Darwin ] && command -v ditto >/dev/null 2>&1; then
    ditto -c -k --sequesterRsrc --keepParent "$1/$2" "$3"      # 保留权限与符号链接
  else
    ( cd "$1" && zip -qryX "$3" "$2" -x "*.DS_Store" )
  fi
}

_zip_file() {  # <文件> <zip>
  ( cd "$(dirname "$1")" && zip -qX "$2" "$(basename "$1")" )
}

# 组装 GC.app：把 stage 目录里的所有产物收进 Contents/MacOS（GC-UI.app 作为子应用嵌入）
package_mac_app() {
  _dist="$1"
  _app="$_dist/GC.app"
  _trash "$_app"
  mkdir -p "$_app/Contents/MacOS" "$_app/Contents/Resources"

  cat > "$_app/Contents/Info.plist" <<PLIST
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
  <!-- 启动器只承担后端宿主，界面在 GC-UI.app：不出现在 Dock，避免出现两个图标 -->
  <key>LSUIElement</key><true/>
</dict>
</plist>
PLIST

  for _f in "$_dist"/*; do
    _base=$(basename "$_f")
    case "$_base" in GC.app|.DS_Store) continue ;; esac
    mv "$_f" "$_app/Contents/MacOS/"
  done

  # 图标：从 icon-1024.png 生成 .icns。生成一次就缓存（原来每次构建都跑十几遍 sips）
  _icon="$GC_ROOT/GC/src/Assets/icon-1024.png"
  _icns="$GC_ROOT/build/cache/AppIcon.icns"
  if [ -f "$_icon" ] && command -v iconutil >/dev/null 2>&1; then
    if [ ! -f "$_icns" ] || [ "$_icon" -nt "$_icns" ]; then
      _set="$GC_ROOT/build/cache/AppIcon.iconset"
      rm -rf "$_set"; mkdir -p "$_set"
      for _sz in 16 32 64 128 256 512; do
        sips -z "$_sz" "$_sz" "$_icon" --out "$_set/icon_${_sz}x${_sz}.png" >/dev/null 2>&1
        _d=$((_sz * 2))
        sips -z "$_d" "$_d" "$_icon" --out "$_set/icon_${_sz}x${_sz}@2x.png" >/dev/null 2>&1
      done
      mkdir -p "$GC_ROOT/build/cache"
      iconutil -c icns "$_set" -o "$_icns" 2>/dev/null || true
      rm -rf "$_set"
      detail "生成 AppIcon.icns"
    else
      detail "复用缓存 AppIcon.icns"
    fi
    if [ -f "$_icns" ]; then
      cp "$_icns" "$_app/Contents/Resources/AppIcon.icns"
      /usr/libexec/PlistBuddy -c "Delete :CFBundleIconFile" "$_app/Contents/Info.plist" 2>/dev/null || true
      /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$_app/Contents/Info.plist" 2>/dev/null || true
    fi
  fi

  # .GC 由首次启动创建；包里只保留 pack_launcher 后补的 .GC/bin
  rm -rf "$_app/Contents/MacOS/.GC"
  chmod +x "$_app/Contents/MacOS/GC" 2>/dev/null || true
}

# 生成全部发布 zip。数据源是 build/stage/<平台>；
# 若 stage 不存在则回落到 dist/<平台>（支持单独跑 ./build.sh pack-launcher）
pack_launcher() {
  say "生成发布包"
  _pkgroot="$GC_ROOT/build/pkg"
  _trash "$_pkgroot"
  mkdir -p "$_pkgroot"

  # ---------- Windows / Linux：整目录打包 ----------
  # 只打包本次构建的「非 mac」平台；mac 走下面的 .app 流程。
  # 单独跑 pack-launcher 时（没有 GC_PACK_LABELS）按全部标签处理。
  _labels=""
  for _l in ${GC_PACK_LABELS:-win-amd64 win-arm64 linux-arm64}; do
    case "$_l" in mac-*) continue ;; esac
    _labels="$_labels $_l"
  done
  for _label in $_labels; do
    _src="$GC_STAGE/$_label"
    _name="GCL-${VERSION}-${_label}"
    _pkg="$_pkgroot/$_name"

    if [ -d "$_src" ]; then
      mv "$_src" "$_pkg" 2>/dev/null || cp -R "$_src" "$_pkg"   # 同分区 rename，零拷贝
    elif [ -z "${GC_PACK_LABELS:-}" ] && [ -d "$GC_ROOT/dist/$_label" ]; then
      cp -R "$GC_ROOT/dist/$_label" "$_pkg"                     # 兼容旧布局
    else
      continue
    fi

    rm -f "$_pkg"/*.zip "$_pkg"/.DS_Store 2>/dev/null || true
    case "$_label" in
      win-*)   prune_package "$_pkg" win ;;
      linux-*) prune_package "$_pkg" linux ;;
    esac

    # 启动器改名成包名；后端与前端是随包产物，不能当成启动器
    _exe="$_pkg/GC.exe"
    [ -f "$_exe" ] || _exe=$(ls "$_pkg"/*.exe 2>/dev/null | grep -v 'GC-SAVEDOG\|GC.UI.Server\|GC-UI' | head -1)
    if [ -n "$_exe" ] && [ -f "$_exe" ]; then
      mv "$_exe" "$_pkg/$_name.exe"
      chmod +x "$_pkg/$_name.exe" 2>/dev/null || true
    elif [ -f "$_pkg/GC" ]; then
      mv "$_pkg/GC" "$_pkg/$_name"
      chmod +x "$_pkg/$_name" 2>/dev/null || true
    else
      _trash "$_pkg"; continue
    fi

    # 非本体文件统一收进 .GC/bin：安装目录根只留 GC 本体（首次启动复制到 .GC/safedog）
    for _osd in GC-SAVEDOG.exe GC-SAVEDOG; do
      if [ -f "$_pkg/$_osd" ]; then
        mkdir -p "$_pkg/.GC/bin"
        mv "$_pkg/$_osd" "$_pkg/.GC/bin/"
      fi
    done
    [ -f "$_pkg/GC-SAVEDOG.exe" ] || [ -f "$_pkg/.GC/bin/GC-SAVEDOG" ] || \
      [ -f "$_pkg/.GC/bin/GC-SAVEDOG.exe" ] || warn "$_label 缺少守护进程，该平台将退回旧脚本更新"
    [ -f "$_pkg/GC-UI.exe" ] || [ -f "$_pkg/GC-UI" ] || \
      note "$_label 包内没有 Flutter 前端，启动器将使用内置 Avalonia 界面"

    _zip="$GC_ROOT/dist/$_label/$_name.zip"
    mkdir -p "$GC_ROOT/dist/$_label"
    # 旧布局会在 dist/<平台>/ 里留裸产物（GC、GC-SAVEDOG、GC-UI.app…），只保留 zip
    for _old in "$GC_ROOT/dist/$_label"/*; do
      [ -e "$_old" ] || continue
      case "$_old" in *.zip) continue ;; esac
      _trash "$_old"
    done
    rm -f "$_zip"
    case "$_label" in
      win-*) _zip_dir "$_pkgroot" "$_name" "$_zip" || return 1 ;;
      *)     ( cd "$_pkgroot" && zip -qryX "$_zip" "$_name" ) || return 1 ;;
    esac
    [ -s "$_zip" ] || { err "$_label 压缩产物为空"; return 1; }
    _trash "$_pkg"
    step_ok "dist/$_label/$_name.zip（$(_hsize "$_zip")）"
  done

  # ---------- macOS：.app 目录 + ditto 压缩 ----------
  _want_mac=1
  if [ -n "${GC_PACK_LABELS:-}" ]; then
    _want_mac=0
    case " ${GC_PACK_LABELS} " in *" mac-arm64 "*) _want_mac=1 ;; esac
  fi
  _stage_mac="$GC_STAGE/mac-arm64"
  if [ "$_want_mac" = 1 ] && [ -d "$_stage_mac/GC.app" ]; then
    _name="GCL-${VERSION}-mac-arm64"
    _app="$_pkgroot/$_name.app"
    mv "$_stage_mac/GC.app" "$_app" 2>/dev/null || cp -R "$_stage_mac/GC.app" "$_app"

    chmod +x "$_app/Contents/MacOS/GC" 2>/dev/null || true
    _plist="$_app/Contents/Info.plist"
    if [ -f "$_plist" ]; then
      /usr/libexec/PlistBuddy -c "Set :CFBundleName Morris GC" "$_plist" 2>/dev/null || true
      /usr/libexec/PlistBuddy -c "Set :CFBundleDisplayName Morris GC" "$_plist" 2>/dev/null || true
    fi
    # 与 Windows 布局一致：守护进程收进 .GC/bin，首次启动复制到 .GC/safedog
    if [ -f "$_app/Contents/MacOS/GC-SAVEDOG" ]; then
      mkdir -p "$_app/Contents/MacOS/.GC/bin"
      mv "$_app/Contents/MacOS/GC-SAVEDOG" "$_app/Contents/MacOS/.GC/bin/"
      chmod +x "$_app/Contents/MacOS/.GC/bin/GC-SAVEDOG" 2>/dev/null || true
    fi
    xattr -cr "$_app" 2>/dev/null || true

    _zip="$GC_ROOT/dist/mac-arm64/$_name.zip"
    mkdir -p "$GC_ROOT/dist/mac-arm64"
    for _old in "$GC_ROOT/dist/mac-arm64"/*; do
      [ -e "$_old" ] || continue
      case "$_old" in *.zip) continue ;; esac
      _trash "$_old"
    done
    rm -f "$_zip"
    _zip_dir "$_pkgroot" "$_name.app" "$_zip" || return 1
    [ -s "$_zip" ] || { err "压缩产物为空"; return 1; }
    _trash "$_app"
    step_ok "dist/mac-arm64/$_name.zip（$(_hsize "$_zip")）"
  fi

  # stage 里剩下的（UI 副本、已搬空的平台目录）一次性挪走
  for _left in "$GC_STAGE"/*; do
    [ -e "$_left" ] || continue
    _trash "$_left"
  done
  find "$GC_ROOT/dist" -name ".DS_Store" -delete >/dev/null 2>&1 || true

  stamp_launcher_only
}

# 把 model/ 里的模型数据单独打包（与启动器分开分发）
pack_models() {
  _zip="$GC_ROOT/dist/models-$VERSION.zip"
  mkdir -p "$GC_ROOT/dist"
  rm -f "$_zip"
  say "打包模型数据"
  ( cd "$GC_ROOT" && zip -qrX "$_zip" model -x "*.DS_Store" "*/bin/*" "*/obj/*" "*.user" ) || return 1
  step_ok "dist/models-$VERSION.zip（$(_hsize "$_zip")）"
}
