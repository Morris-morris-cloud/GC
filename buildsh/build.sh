#!/bin/sh
# buildsh/build.sh —— 完整发布
#
# 目标平台（默认按构建主机推断，可用 --targets= 覆盖）：
#   macOS 主机 : mac-arm64（原生） + win-amd64 / win-arm64（mingw 交叉）
#   Linux 主机 : win-amd64 / win-arm64（mingw 交叉）
#                Linux 产物默认不构建（目标 linux-arm64，官方 Flutter 引擎只有 x64，
#                必须在 aarch64 主机上编）；需要时 GC_BUILD_LINUX=1 显式开启
#   Windows 主机: win-amd64 / win-arm64（原生）
#
#   ./build.sh build                 全平台
#   ./build.sh build --targets=mac   只出 macOS（跳过 Windows 交叉编译，秒级省下几分钟）
#   ./build.sh build --targets=win   只出 Windows
#
# 前端是必需项：flutter 缺失或编译失败一律中断，不出只有 Avalonia 回退界面的包。
# 确实要降级时用 ./build.sh build --allow-no-ui。
#
# 并行：Flutter 前端编译 与 C# 各 RID 发布 互不依赖，两组同时跑。

# ---------------------------------------------------------------- 目标与权重

_resolve_targets() {
  BUILD_MAC=0; BUILD_WIN=0; BUILD_LINUX=0

  case "$(uname -s)" in
    Darwin)               _host="mac" ;;
    MINGW*|MSYS*|CYGWIN*) _host="windows" ;;
    *)                    _host="linux" ;;
  esac
  GC_HOST="$_host"

  _want="${GC_TARGETS:-}"
  if [ -z "$_want" ]; then
    case "$_host" in
      mac)     _want="mac,win" ;;
      windows) _want="win" ;;
      *)       _want="win" ;;
    esac
  fi

  _ifs=$IFS; IFS=','
  for _t in $_want; do
    case "$_t" in
      ''|' ') continue ;;
      mac|macos|darwin) BUILD_MAC=1 ;;
      win|windows)      BUILD_WIN=1 ;;
      linux)            BUILD_LINUX=1 ;;
      all)              BUILD_MAC=1; BUILD_WIN=1; BUILD_LINUX=1 ;;
      *) IFS=$_ifs; err "未知目标 '$_t'（可用：mac / win / linux / all）"; return 1 ;;
    esac
  done
  IFS=$_ifs

  if [ "$BUILD_MAC" = 1 ] && [ "$_host" != mac ]; then
    err "macOS 产物只能在 macOS 上编译（当前 $(uname -s)）"; return 1
  fi

  # Linux 产物：必须是 aarch64 主机且显式开启
  if [ "$BUILD_LINUX" = 1 ]; then
    case "${GC_BUILD_LINUX:-0}" in
      1|true|yes) ;;
      *) BUILD_LINUX=0; detail "跳过 Linux 产物（需要时 GC_BUILD_LINUX=1）" ;;
    esac
    if [ "$BUILD_LINUX" = 1 ]; then
      case "$(uname -m)" in
        aarch64|arm64) ;;
        *) BUILD_LINUX=0
           warn "已请求 Linux 产物，但当前主机是 $(uname -m)（linux-arm64 需要 aarch64 主机）" ;;
      esac
    fi
  fi

  # 发布计划：RID:标签
  _PLAN=""
  [ "$BUILD_MAC" = 1 ]   && _PLAN="$_PLAN osx-arm64:mac-arm64"
  [ "$BUILD_WIN" = 1 ]   && _PLAN="$_PLAN win-x64:win-amd64 win-arm64:win-arm64"
  [ "$BUILD_LINUX" = 1 ] && _PLAN="$_PLAN linux-arm64:linux-arm64"

  [ -n "$_PLAN" ] || { err "没有要构建的目标"; return 1; }

  # 打包阶段只处理本次构建的平台，避免把 dist 里其它平台的旧残留也重新打包
  GC_PACK_LABELS=""
  for _t in $_PLAN; do GC_PACK_LABELS="$GC_PACK_LABELS ${_t##*:}"; done
  export GC_PACK_LABELS
}

_init_weights() {
  _ws="3 52"                                  # 准备 / 编译发布
  [ "$BUILD_WIN" = 1 ] && _ws="$_ws 8"        # Win 前端交叉编译
  _ws="$_ws 7 27 3"                           # 组装 / 打包 / 收尾
  _tot=0
  for _w in $_ws; do _tot=$((_tot + _w)); done
  progress_init "$_tot" $_ws
}

# 编译阶段预估秒数（只用于进度条推进，不影响正确性）
_est_compile() {
  _n=0
  for _t in $_PLAN; do _n=$((_n + 1)); done
  _e=$((_n * 70))
  [ "$BUILD_MAC" = 1 ] && _e=$((_e + 60))
  printf '%s' "$_e"
}

# ---------------------------------------------------------------- 脚本自检

# `$VAR` 紧跟全角字符（如 "）"）时，变量名会把多字节字符一起吃进去，
# set -u 下直接报 "_plat?: unbound variable"。这个坑历史上出现过两次，
# 交给机器每轮查一遍比靠人眼可靠（找不到 python3 就跳过）。
_lint_scripts() {
  command -v python3 >/dev/null 2>&1 || { detail "无 python3，跳过脚本自检"; return 0; }
  python3 - "$GC_ROOT" <<'PY'
import re, sys, glob, os

root = sys.argv[1]
pat = re.compile(r'(?<!\$\{)\$([A-Za-z_][A-Za-z0-9_]*)([^\x00-\x7f])')
files = [os.path.join(root, 'build.sh')]
files += glob.glob(os.path.join(root, 'buildsh', '**', '*.sh'), recursive=True)

bad = []
for f in files:
    try:
        lines = open(f, encoding='utf-8').read().splitlines()
    except OSError:
        continue
    for i, line in enumerate(lines, 1):
        if line.lstrip().startswith('#'):      # 整行注释里的 $符号不会参与解析
            continue
        for m in pat.finditer(line):
            bad.append('%s:%d: $%s 后接 %r' % (os.path.relpath(f, root), i, m.group(1), m.group(2)))

if bad:
    sys.stderr.write('  脚本里变量名后紧跟全角字符（请改成 ${VAR}）：\n')
    for b in bad:
        sys.stderr.write('    ' + b + '\n')
    sys.exit(1)
PY
}

# ---------------------------------------------------------------- 阶段

# 各 RID 发布 + macOS 前端：两条链互不依赖，同时进行
_phase_compile() {
  _fbg=""
  if [ "$BUILD_MAC" = 1 ]; then
    # 后台编前端；GC_QUIET 避免它的输出打乱进度条
    ( GC_QUIET=1; flutter_build macos ) &
    _fbg=$!
  fi

  _rc=0
  for _t in $_PLAN; do
    publish_rid "${_t%%:*}" "${_t##*:}" || { _rc=1; break; }
  done

  if [ -n "$_fbg" ]; then
    if [ "$_rc" != 0 ]; then
      kill "$_fbg" 2>/dev/null || true
    else
      if wait "$_fbg"; then
        flutter_stage macos "$GC_STAGE/mac-arm64" || _rc=1
      else
        err "Flutter macOS 前端编译失败（日志：build/logs/flutter-macos.log）"
        _rc=1
      fi
    fi
  fi
  return "$_rc"
}

# Windows 前端：只产出一份 x64，两个包共用（ARM64 上由系统模拟 x64 进程运行）。
# Windows 主机走官方 toolchain，其它主机走 mingw + wine 交叉编译。
_phase_win_ui() {
  if [ "$GC_HOST" = windows ]; then
    ( GC_QUIET=1; build_flutter_ui windows "$GC_STAGE/win-flutter" ) || return 1
  else
    build_flutter_ui_cross "$GC_STAGE/win-flutter" || return 1
  fi
  for _l in win-amd64 win-arm64; do
    [ -d "$GC_STAGE/$_l" ] || continue
    cp -R "$GC_STAGE/win-flutter/." "$GC_STAGE/$_l/" || return 1
  done
  _trash "$GC_STAGE/win-flutter"
}

_phase_assemble() {
  for _l in win-amd64 win-arm64 linux-arm64; do
    prepare_runtime_layout "$GC_STAGE/$_l"
  done
  if [ -d "$GC_STAGE/mac-arm64" ]; then
    prepare_runtime_layout "$GC_STAGE/mac-arm64"
    package_mac_app "$GC_STAGE/mac-arm64" || return 1
    step_ok "GC.app 组装完成"
  fi
}

_summary() {
  _span=$(timer_span "$_t_all")
  printf '\n'
  say "完成，用时 ${_span}"
  _found=0
  for _z in "$GC_ROOT"/dist/*/*.zip; do
    [ -f "$_z" ] || continue
    _found=1
    printf '    %6s  %s\n' "$(_hsize "$_z")" "$(_rel "$_z")"
  done
  [ "$_found" = 1 ] || note "（dist 下没有产物）"
}

# ---------------------------------------------------------------- 入口

bs_build() {
  ROOT_DIR="$1"
  GC_ROOT="$ROOT_DIR"; export GC_ROOT
  GC_STAGE="$ROOT_DIR/build/stage"; export GC_STAGE
  : "${APP_PROJ:?}" "${VERSION:?}"

  _resolve_targets || return 1
  preflight || return 1
  _init_weights
  _t_all=$(timer_start)

  # ---- 准备：清暂存区 + 生成清单（不签名，打包结束后统一签一次）
  progress_step "准备"
  mkdir -p "$GC_ROOT/build/logs" "$GC_ROOT/build/cache"
  if [ -d "$GC_ROOT/build/trash" ]; then
    mv "$GC_ROOT/build/trash" "${TMPDIR:-/tmp}/gc-trash-$(date +%s)" 2>/dev/null || true
  fi
  _trash "$GC_STAGE"
  mkdir -p "$GC_STAGE"
  _trash "$GC_ROOT/build/pkg"
  detail "目标：$(printf '%s' "$_PLAN" | tr ' ' '\n' | cut -d: -f2 | tr '\n' ' ')"
  _lint_scripts || return 1
  stamp_versions --no-sign || return 1
  progress_tick 100

  # ---- 编译：各 RID 发布 ‖ Flutter mac 前端
  progress_step "编译"
  _run_phase "$(_est_compile)" _phase_compile || return 1

  # ---- Windows 前端（mac 前端编完后才启动，避免两个 flutter 抢 SDK 锁）
  if [ "$BUILD_WIN" = 1 ]; then
    progress_step "Win 前端"
    _run_phase 70 _phase_win_ui || return 1
  fi

  # ---- 组装：运行目录布局 + mac .app
  progress_step "组装"
  _phase_assemble || return 1
  progress_tick 100

  # ---- 打包：zip + 清单 sha256 + 签名
  progress_step "打包"
  pack_launcher || return 1
  progress_tick 100

  # ---- 收尾
  progress_step "收尾"
  progress_done
  _summary
}
