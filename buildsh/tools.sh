#!/bin/sh
# buildsh/tools.sh —— 实用工具：模型、版本戳、清理、运行、医生、打开目录

bs_tools() {
  ROOT_DIR="$1"; TASK="$2"
  case "$TASK" in
    models)
      build_models ;;
    stamp)
      stamp_versions ;;
    pack-models)
      build_models; pack_models ;;
    pack-launcher)
      pack_launcher ;;
    run)
      dotnet run --project "$APP_PROJ" -c Debug ;;
    test)
      dotnet run --project "$TEST_PROJ" -c Debug -- "$ROOT_DIR/build/screenshots" ;;
    update-test)
      dotnet run --project "$TEST_PROJ" -c Debug -- --update-test "$ROOT_DIR" ;;
    clean)
      rm -rf "$ROOT_DIR/build" "$ROOT_DIR/dist"
      find "$ROOT_DIR/src" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
      echo "==> 已清理" ;;
    doctor)
      dotnet --info | head -20 ;;
    dist)
      echo "==> dist 产物："
      ls -lh "$ROOT_DIR"/dist/*/GCL-*.zip 2>/dev/null || ls -lh "$ROOT_DIR"/dist 2>/dev/null || true ;;
    *)
      echo "未知工具: $TASK"; return 1 ;;
  esac
}
