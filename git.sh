#!/bin/sh
# ---------------------------------------------------------------------------
# git.sh —— GC 仓库的 git 导航入口
#
#   ./git.sh                 显示帮助（列出 gitshell/ 里所有可用命令）
#   ./git.sh <命令> [参数]    调用 gitshell/<命令>.sh
#
# 例：
#   ./git.sh autogit          提交并推送到所有已配置的仓库
#   ./git.sh autogit -m "修复" 指定提交信息
#
# 实际脚本都在 gitshell/ 里，本文件只负责查找与分发（与 build.sh 同样的设计）。
# ---------------------------------------------------------------------------
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$ROOT_DIR"

SHELL_DIR="$ROOT_DIR/gitshell"

list_commands() {
  echo "GC git 工具（gitshell/）"
  echo
  if [ -d "$SHELL_DIR" ]; then
    for f in "$SHELL_DIR"/*.sh; do
      [ -f "$f" ] || continue
      name=$(basename "$f" .sh)
      # 取文件头第一条「# 描述」行（跳过 shebang 与空注释）
      desc=$(sed -n '2,6p' "$f" | sed -n 's/^# *\(.\)/\1/p' | grep -v '^$' | head -1)
      printf '  %-14s %s\n' "$name" "${desc:-}"
    done
  else
    echo "  （gitshell/ 目录不存在）"
  fi
  echo
  echo "用法：./git.sh <命令> [参数]"
}

CMD="${1:-}"
[ -n "$CMD" ] || { list_commands; exit 0; }
case "$CMD" in
  -h|--help|help) list_commands; exit 0 ;;
esac
shift 2>/dev/null || true

SCRIPT="$SHELL_DIR/$CMD.sh"
if [ ! -f "$SCRIPT" ]; then
  echo "!! 没有这个命令：$CMD"
  echo
  list_commands
  exit 1
fi

exec sh "$SCRIPT" "$ROOT_DIR" "$@"
