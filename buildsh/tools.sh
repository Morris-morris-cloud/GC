#!/bin/sh
# buildsh/tools.sh —— 实用工具：模型、版本戳、打包、清理、运行、医生

bs_tools() {
  ROOT_DIR="$1"; TASK="${2:-}"
  GC_ROOT="${GC_ROOT:-$ROOT_DIR}"; export GC_ROOT
  GC_STAGE="${GC_STAGE:-$ROOT_DIR/build/stage}"; export GC_STAGE

  case "$TASK" in
    models)
      build_models ;;
    stamp)
      stamp_versions ;;
    pack-models)
      build_models; pack_models ;;
    pack-launcher)
      # 单独打包：数据源优先 build/stage，缺失时回落到 dist
      mkdir -p "$GC_ROOT/build/logs"
      pack_launcher ;;
    run)
      dotnet run --project "$APP_PROJ" -c Debug ;;
    clean)
      say "清理构建产物"
      _trash "$ROOT_DIR/build"
      _trash "$ROOT_DIR/dist"
      find "$ROOT_DIR/src" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + >/dev/null 2>&1 || true
      step_ok "已清理" ;;
    doctor)
      dotnet --info | head -20 ;;
    dist)
      say "dist 产物"
      _found=0
      for _z in "$ROOT_DIR"/dist/*/*.zip; do
        [ -f "$_z" ] || continue
        _found=1
        printf '    %6s  %s\n' "$(_hsize "$_z")" "$(_rel "$_z")"
      done
      [ "$_found" = 1 ] || note "（暂无产物）" ;;
    *)
      err "未知工具：${TASK:-（空）}"
      note "可用：models / stamp / pack-models / pack-launcher / run / clean / doctor / dist"
      return 1 ;;
  esac
}
