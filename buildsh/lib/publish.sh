#!/bin/sh
# buildsh/lib/publish.sh —— 环境预检 / 工具程序集缓存 / dotnet 发布
#
# 两个关键设计：
#   1. dt_tool：工具类程序（VersionStamp / PreviewGen / GC-SAVEDOG 签名）只 build 一次，
#      之后直接 dotnet <dll> 运行。原来每次 dotnet run 都要走一遍 MSBuild 评估
#      （即便没有任何改动），一次构建里能省下好几秒和一屏还原输出。
#   2. _trash：大目录一律"挪进 build/trash"而不是递归删除。几百个文件的 rm -rf
#      既慢又不可逆，挪走更安全（下次构建开始时会整体移到 TMPDIR）。

# ---------------------------------------------------------------- 环境预检

need() { command -v "$1" >/dev/null 2>&1; }

# 一次性检查所有依赖，缺什么全部列出来立刻退出。
# 目的：不要跑到第 5 步（交叉编译）才发现没装 cmake，白白浪费前面几分钟。
preflight() {
  _miss=""
  _add() { _miss="$_miss
  - $1（$2）"; }

  need dotnet || _add dotnet "安装 .NET 8 SDK：https://dot.net"
  need zip    || _add zip "Linux: apt install zip；macOS: 系统自带"

  if [ "$BUILD_MAC" = 1 ] || [ "$BUILD_WIN" = 1 ]; then
    need "${FLUTTER_BIN:-flutter}" || _add flutter "设置 FLUTTER_BIN=<路径> 或加入 PATH"
  fi

  # Windows 前端交叉编译（非 Windows 主机）
  if [ "$BUILD_WIN" = 1 ] && [ "$GC_HOST" != windows ]; then
    need cmake  || _add cmake "Linux: apt install cmake；macOS: brew install cmake"
    need ninja  || _add ninja "Linux: apt install ninja-build；macOS: brew install ninja"
    need unzip  || _add unzip "Linux: apt install unzip"
    need curl   || _add curl  "Linux: apt install curl"
    need x86_64-w64-mingw32-windres \
      || _add mingw-w64 "Linux: apt install g++-mingw-w64-x86-64-posix；macOS: brew install mingw-w64"
    need wine || need wine64 \
      || _add wine "Linux: apt install wine；macOS: 见 buildsh/cross/build-ui-win-cross.sh 顶部说明"
  fi

  if [ -n "$_miss" ]; then
    err "构建环境不完整，缺少：$_miss"
    note "装好后重试。只需要 macOS 版可用：./build.sh build --targets=mac"
    return 1
  fi
}

# ---------------------------------------------------------------- 工具程序集

# 源码比产物新？用于决定是否重新构建工具
_any_newer() {
  [ -n "$(find "$1" \( -name bin -o -name obj \) -prune -o \
      \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) \
      -newer "$2" -print -quit 2>/dev/null)" ]
}

# dt_tool <缓存名> <项目相对路径> <程序集名> [参数...]
dt_tool() {
  _tn="$1"; _tp="$2"; _ta="$3"; shift 3
  _tout="$GC_ROOT/build/tools/$_tn"
  _tdll="$_tout/$_ta.dll"
  mkdir -p "$GC_ROOT/build/tools"

  if [ ! -f "$_tdll" ] || _any_newer "$GC_ROOT/$(dirname "$_tp")" "$_tdll"; then
    detail "构建工具 $_tn"
    run_logged "$GC_ROOT/build/logs/tool-$_tn.log" "构建 $_tn" \
      dotnet build "$GC_ROOT/$_tp" -c Release -o "$_tout" --nologo -v:q || return 1
  fi
  dotnet "$_tdll" "$@"
}

# ---------------------------------------------------------------- 目录清理

# 把目录挪进 build/trash（同分区 rename，几乎零成本），避免批量递归删除
_trash() {
  [ -e "$1" ] || return 0
  _tr="$GC_ROOT/build/trash"
  mkdir -p "$_tr" 2>/dev/null || true
  mv "$1" "$_tr/$(basename "$1")-$(date +%s)-$$" 2>/dev/null || rm -rf "$1"
  return 0
}

# ---------------------------------------------------------------- 发布

# publish_rid <RID> <标签>
# 产物落 build/stage/<标签>/，后续打包直接就地改名 + 压缩，
# 不再 dist → 暂存目录 的整份复制（原来每个平台白白多搬 200MB）。
publish_rid() {
  _rid="$1"; _label="$2"
  _out="$GC_STAGE/$_label"

  _trash "$_out"
  mkdir -p "$_out"

  run_logged "$GC_ROOT/build/logs/publish-$_label.log" "$_rid 启动器 " \
    dotnet publish "$GC_ROOT/$APP_PROJ" \
      -c Release -r "$_rid" --self-contained true \
      -p:Version="$VERSION" -p:DebugType=none \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:EnableCompressionInSingleFile=true \
      -p:PublishTrimmed=false \
      --nologo -v:m -o "$_out" || return 1

  # 守护进程：单独的发布目录，只把单文件本体取出来放进包
  # （同目录发布会把两套运行时文件混在一起，也没法并行）
  if [ -n "${SAVEDOG_PROJ:-}" ] && [ -f "$GC_ROOT/$SAVEDOG_PROJ" ]; then
    _sd="$GC_ROOT/build/tmp/savedog-$_label"
    _trash "$_sd"
    mkdir -p "$_sd"
    # 裁剪 + 压缩单文件：70MB -> 14MB。JSON 全走源生成（GC-SAVEDOG/SavedogJson.cs），
    # 新增类型忘注册只会在发布产物里炸，所以 update-test 会跑一次真实 apply 兜底。
    run_logged "$GC_ROOT/build/logs/publish-savedog-$_label.log" "$_rid 守护进程 " \
      dotnet publish "$GC_ROOT/$SAVEDOG_PROJ" \
        -c Release -r "$_rid" --self-contained true \
        -p:Version="$VERSION" -p:DebugType=none \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:PublishTrimmed=true -p:TrimMode=full \
        -p:JsonSerializerIsReflectionEnabledByDefault=false \
        --nologo -v:m -o "$_sd" || return 1
    for _f in "$_sd/GC-SAVEDOG" "$_sd/GC-SAVEDOG.exe"; do
      [ -f "$_f" ] && cp "$_f" "$_out/"
    done
    _trash "$_sd"
  fi

  rm -rf "$_out/model" "$_out/examples"
  step_ok "$_rid"
}

# _run_phase <预估秒数> <函数> [参数...]
# 在后台跑一个阶段，前台每秒刷新进度条；阶段退出码如实返回。
_run_phase() {
  _rp_est="$1"; shift
  ( "$@" ) &
  _rp_pid=$!
  _rp_t0=$(date +%s)
  while kill -0 "$_rp_pid" 2>/dev/null; do
    _rp_p=$(( ($(date +%s) - _rp_t0) * 100 / _rp_est ))
    [ "$_rp_p" -gt 96 ] && _rp_p=96
    progress_tick "$_rp_p"
    sleep 1
  done
  wait "$_rp_pid" || return 1
  progress_tick 100
}
