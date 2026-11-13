#!/bin/sh
# autogit —— 提交并推送到所有已配置的仓库（一键多仓同步）
#
# 用法：
#   ./git.sh autogit                     自动生成提交信息并推送所有仓库
#   ./git.sh autogit -m "提交信息"        指定提交信息
#   ./git.sh autogit -m "..." -n          只提交到暂存区，不推送
#   ./git.sh autogit --dry-run           只看会提交什么，不动仓库
#
# 多仓库：脚本会遍历 `git remote` 里的**每一个**远程，依次 push。
#   加仓库： git remote add gitee git@github.com:user/repo.git
#   看仓库： git remote -v
#
# 自动跳过（永不提交）：dist/ build/ bin/ obj/ .GC/ node_modules/ 等构建产物与运行数据
# ---------------------------------------------------------------------------
set -eu

ROOT_DIR="${1:-.}"
shift 2>/dev/null || true
cd "$ROOT_DIR"

MSG=""
PUSH=1
DRY=0
while [ $# -gt 0 ]; do
  case "$1" in
    -m|--message) MSG="${2:-}"; shift 2 ;;
    -n|--no-push) PUSH=0; shift ;;
    --dry-run) DRY=1; shift ;;
    *) echo "未知参数：$1"; exit 1 ;;
  esac
done

# ---------- 0. 前置检查 ----------
git rev-parse --git-dir >/dev/null 2>&1 || { echo "!! 这里不是 git 仓库：$(pwd)"; exit 1; }

BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
[ -n "$BRANCH" ] && [ "$BRANCH" != "HEAD" ] || { echo "!! 当前不在任何分支上"; exit 1; }

# ---------- 1. 确保忽略规则齐全 ----------
# 这些目录即使历史上被跟踪，也不会被本次提交带上；新增的会被 .gitignore 挡住
ensure_ignore() {
  pattern="$1"
  grep -qxF "$pattern" .gitignore 2>/dev/null && return 0
  printf '%s\n' "$pattern" >> .gitignore
  echo "    + .gitignore 追加 $pattern"
}
if [ -f .gitignore ]; then
  for p in "dist/" "build/" "bin/" "obj/" ".GC/"; do ensure_ignore "$p"; done
fi

# ---------- 2. 收集改动 ----------
echo "==> 检查改动（分支 ${BRANCH}）"

# --dry-run：只统计，绝不改动索引
if [ "$DRY" = "1" ]; then
  echo
  echo "（--dry-run）会被移出索引的构建产物："
  for d in dist build bin obj .GC; do
    C=$(git ls-files -- "*$d/*" 2>/dev/null | wc -l | tr -d ' ')
    [ "$C" != "0" ] && echo "    $d/  $C 个文件"
  done
  echo
  echo "（--dry-run）当前未提交的改动："
  git status --porcelain | grep -vE "/(dist|build|bin|obj|\\.GC)/" | head -20 | sed 's/^/    /'
  echo
  echo "==> --dry-run：未做任何修改"
  exit 0
fi

# 关键顺序：先把「历史上被跟踪、但现在应忽略」的目录移出索引，再 add。
# 反过来的话，git add -A 会把 bin/obj 的改动重新加回暂存区。
# 用通配匹配任意层级的目录（如 model/xxx/src/bin），不只是仓库根
for d in dist build bin obj .GC; do
  TRACKED=$(git ls-files -- "*$d/*" 2>/dev/null | head -1)
  if [ -n "$TRACKED" ]; then
    COUNT=$(git ls-files -- "*$d/*" 2>/dev/null | wc -l | tr -d ' ')
    git rm -r --cached --quiet -- "*$d/*" 2>/dev/null || true
    # 根目录同名目录也处理一下
    git rm -r --cached --quiet -- "$d" 2>/dev/null || true
    echo "    - 从索引移除 $COUNT 个 $d/ 内的文件（本地保留）"
  fi
done

# 再收集其余改动（被忽略的目录不会进来）
git add -A 2>/dev/null || true

STAGED=$(git diff --cached --name-only | wc -l | tr -d ' ')
if [ "$STAGED" = "0" ]; then
  echo "==> 没有需要提交的改动"
  [ "$PUSH" = "1" ] && [ "$DRY" = "0" ] && { echo "==> 仍尝试推送未推送的提交"; }
else
  echo "==> 待提交 $STAGED 个文件"
  git diff --cached --name-status | sed 's/^/    /' | head -30
  [ "$STAGED" -gt 30 ] && echo "    ...（其余 $((STAGED - 30)) 个略）"
fi

# ---------- 3. 提交 ----------
if [ "$STAGED" != "0" ]; then
  if [ -z "$MSG" ]; then
    # 自动提交信息：时间 + 主要改动文件类别
    KIND=$(git diff --cached --name-only \
      | sed 's|/.*||' | sort -u | head -3 | tr '\n' ' ')
    MSG="更新 $KIND($(date '+%Y-%m-%d %H:%M'))"
  fi
  git commit -q -m "$MSG"
  echo "==> 已提交：$MSG"
else
  echo "==> 跳过提交（无改动）"
fi

# ---------- 4. 推送到所有远程 ----------
if [ "$PUSH" = "0" ]; then
  echo "==> -n 指定不推送，结束"
  exit 0
fi

REMOTES=$(git remote)
if [ -z "$REMOTES" ]; then
  echo "!! 没有配置任何远程仓库，跳过推送"
  exit 0
fi

FAILED=0
for R in $REMOTES; do
  echo "==> 推送到 $R"
  if git push -u "$R" "$BRANCH" 2>&1 | sed 's/^/    /'; then
    echo "    OK"
  else
    # 常见情况：远程有本地没有的提交（非快进）
    echo "    !! 推送失败。若确认要以本地为准，可执行："
    echo "       git push --force-with-lease $R $BRANCH"
    FAILED=$((FAILED + 1))
  fi
done

echo
if [ "$FAILED" -eq 0 ]; then
  echo "==> 全部仓库已同步"
else
  echo "==> 完成，但有 $FAILED 个仓库推送失败"
  exit 1
fi
