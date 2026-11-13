#!/bin/sh
# status —— 一眼看清所有仓库的同步状态
#
# 用法：./git.sh status
#
# 显示：当前分支、改动文件、与每个远程的领先/落后关系。

set -eu
ROOT_DIR="${1:-.}"
cd "$ROOT_DIR"

git rev-parse --git-dir >/dev/null 2>&1 || { echo "!! 不是 git 仓库"; exit 1; }

BRANCH=$(git rev-parse --abbrev-ref HEAD)
echo "仓库：$(pwd)"
echo "分支：$BRANCH"
echo

CHANGED=$(git status --porcelain | wc -l | tr -d ' ')
if [ "$CHANGED" = "0" ]; then
  echo "工作区：干净"
else
  echo "工作区：$CHANGED 处改动"
  git status --short | head -15 | sed 's/^/    /'
  [ "$CHANGED" -gt 15 ] && echo "    ...（其余 $((CHANGED - 15)) 处略）"
fi
echo

# 最近提交
echo "最近提交："
git log --oneline -3 | sed 's/^/    /'
echo

# 每个远程的同步状态
echo "远程仓库："
for R in $(git remote); do
  URL=$(git remote get-url "$R" 2>/dev/null || echo "?")
  AHEAD=$(git rev-list --count "origin/$BRANCH..HEAD" 2>/dev/null || echo "?")
  BEHIND=$(git rev-list --count "HEAD..origin/$BRANCH" 2>/dev/null || echo "?")
  if [ "$R" = "origin" ] && [ "$AHEAD" != "?" ]; then
    printf '    %-10s 领先 %s / 落后 %s   %s\n' "$R" "$AHEAD" "$BEHIND" "$URL"
  else
    printf '    %-10s %s\n' "$R" "$URL"
  fi
done
