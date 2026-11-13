#!/bin/sh
# autogit —— 交互式提交：仓库 → 地址 → 分支 → 信息 → 确认 → 执行
#
# 用法：
#   ./git.sh autogit              交互模式
#   ./git.sh autogit --dry-run    只看会提交什么，不动仓库
#
# 交互流程（严格按此顺序）：
#   1. 选择要提交到哪个仓库（或直接输入仓库地址）
#   2. 确认该仓库地址
#   3. 确认提交分支（可改成其它分支）
#   4. 输入提交信息
#   5. 汇总确认后执行
#
# 自动跳过：dist/ build/ bin/ obj/ .GC/ node_modules/（任意层级）
# ---------------------------------------------------------------------------
set -eu

ROOT_DIR="${1:-.}"
shift 2>/dev/null || true
cd "$ROOT_DIR"

DRY=0
ASSUME_YES=0
MSG=""
while [ $# -gt 0 ]; do
  case "$1" in
    -m|--message) MSG="${2:-}"; shift 2 ;;
    --dry-run) DRY=1; shift ;;
    -y|--yes) ASSUME_YES=1; shift ;;
    -h|--help) sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数：$1"; exit 1 ;;
  esac
done

line() { echo "----------------------------------------------"; }

git rev-parse --git-dir >/dev/null 2>&1 || { echo "!! 这里不是 git 仓库：$(pwd)"; exit 1; }
CUR_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "main")
[ "$CUR_BRANCH" = "HEAD" ] && CUR_BRANCH="main"

# ============ 1. 选择仓库（支持多个）============
echo "=============================================="
echo " 仓库目录：$(pwd)"
echo " 当前分支：$CUR_BRANCH"
echo "=============================================="
echo

REMOTES=$(git remote 2>/dev/null || true)
if [ -n "$REMOTES" ]; then
  echo "已配置的仓库："
  i=0
  for R in $REMOTES; do
    i=$((i + 1))
    URL=$(git remote get-url "$R" 2>/dev/null || echo "?")
    printf '  %d) %-10s %s\n' "$i" "$R" "$URL"
  done
  echo
  echo "  a) 全部"
  echo "  n) 添加新仓库地址"
  echo
  printf '请选择（可多选，用空格或逗号分隔，如 1 3  或  a  或  n；默认 1）：'
  read -r pick || pick=""
  [ -z "$pick" ] && pick=1
else
  pick="n"
fi

SELECTED=""

# 按编号取远程名
name_of() {
  idx="$1"; k=0
  for R in $REMOTES; do
    k=$((k + 1))
    [ "$k" = "$idx" ] && { echo "$R"; return 0; }
  done
  echo ""
}

add_remote() {
  url="$1"
  [ -n "$url" ] || return 0
  printf '  给 %s 起个名字（回车自动命名）：' "$url"
  read -r nm || nm=""
  if [ -z "$nm" ]; then
    # 从地址里提取仓库名做默认名
    base=$(echo "$url" | sed 's|\.git$||' | sed 's|.*[/:]||')
    nm="${base:-remote}"
  fi
  if git remote get-url "$nm" >/dev/null 2>&1; then
    git remote set-url "$nm" "$url"
    echo "    已更新 $nm -> $url"
  else
    git remote add "$nm" "$url"
    echo "    已添加 $nm -> $url"
  fi
  SELECTED="$SELECTED $nm"
}

case "$pick" in
  a|A)
    for R in $REMOTES; do SELECTED="$SELECTED $R"; done
    ;;
  n|N)
    echo
    echo "可以连续输入多个地址，每行一个，输入空行结束："
    while :; do
      printf '  地址：'
      read -r u || u=""
      [ -z "$u" ] && break
      add_remote "$u"
    done
    ;;
  *)
    # 支持 "1 3" / "1,3" / "1、3"
    for tok in $(echo "$pick" | tr ',，、' '   '); do
      case "$tok" in
        a|A) for R in $REMOTES; do SELECTED="$SELECTED $R"; done ;;
        n|N) echo "  请在下一行输入地址："
             printf '  地址：'
             read -r u || u=""
             add_remote "$u" ;;
        *)
          nm=$(name_of "$tok")
          if [ -n "$nm" ]; then SELECTED="$SELECTED $nm"
          else echo "    !! 无效编号：$tok"; fi
          ;;
      esac
    done
    ;;
esac

# 去重
SELECTED=$(echo "$SELECTED" | tr ' ' '\n' | awk 'NF' | awk '!seen[$0]++' | tr '\n' ' ' | sed 's/^ *//;s/ *$//')
[ -n "$SELECTED" ] || { echo "!! 没有选择任何仓库"; exit 1; }

# ============ 2. 确认仓库地址 ============
echo
line
echo " 目标仓库（共 $(echo "$SELECTED" | wc -w | tr -d ' ') 个）："
for R in $SELECTED; do
  printf '   %-10s %s\n' "$R" "$(git remote get-url "$R" 2>/dev/null || echo '?')"
done
line
printf '以上地址都正确吗？(Y/n) '
read -r okaddr || okaddr=""
case "$okaddr" in
  n|N)
    echo
    echo "逐个修改（直接回车跳过不改）："
    for R in $SELECTED; do
      cur=$(git remote get-url "$R" 2>/dev/null || echo "")
      printf '  %s [%s] -> ' "$R" "$cur"
      read -r nu || nu=""
      [ -n "$nu" ] && git remote set-url "$R" "$nu" && echo "      已更新"
    done
    ;;
esac

# ============ 3. 确认分支 ============
echo
printf '提交分支（当前 %s，直接回车沿用，或输入其它分支名）：' "$CUR_BRANCH"
read -r BRANCH_IN || BRANCH_IN=""
BRANCH="${BRANCH_IN:-$CUR_BRANCH}"

if [ "$BRANCH" != "$CUR_BRANCH" ]; then
  echo "    将创建/切换到分支 $BRANCH"
  if git show-ref --verify --quiet "refs/heads/$BRANCH"; then
    git checkout -q "$BRANCH"
  else
    git checkout -q -b "$BRANCH"
  fi
  echo "    当前分支：$(git rev-parse --abbrev-ref HEAD)"
fi
line
echo " 提交分支：$BRANCH"
line
printf '分支正确吗？(Y/n) '
read -r okbr || okbr=""
case "$okbr" in
  n|N) echo "已取消"; exit 0 ;;
esac

# ============ 4. 改动清单 ============
echo
echo "自动跳过：dist/  build/  bin/  obj/  .GC/"
echo
echo "待提交的改动："
CHANGES=$(git status --porcelain 2>/dev/null | grep -vE "/(dist|build|bin|obj|\.GC)/" || true)
if [ -z "$CHANGES" ]; then
  echo "    （没有改动）"
else
  echo "$CHANGES" | head -25 | sed 's/^/    /'
  N=$(echo "$CHANGES" | wc -l | tr -d ' ')
  [ "$N" -gt 25 ] && echo "    ...（共 $N 项）"
fi
echo

if [ "$DRY" = "1" ]; then
  echo "==> --dry-run：未做任何修改"
  exit 0
fi

# ============ 5. 提交信息 ============
if [ -z "$MSG" ]; then
  printf '请输入提交信息：'
  read -r MSG || MSG=""
fi
if [ -z "$MSG" ]; then
  MSG="更新（$(date '+%Y-%m-%d %H:%M')）"
  echo "    （留空，自动生成：$MSG）"
fi

# ============ 6. 最终确认 ============
echo
echo "=============================================="
echo " 分支：    $BRANCH"
echo " 信息：    $MSG"
echo " 推送到："
for R in $SELECTED; do
  printf '   - %-10s %s\n' "$R" "$(git remote get-url "$R" 2>/dev/null || echo '?')"
done
echo "=============================================="
if [ "$ASSUME_YES" != "1" ]; then
  printf '确认提交并推送？(y/N) '
  read -r go || go=""
  case "$go" in
    y|Y) ;;
    *) echo "已取消"; exit 0 ;;
  esac
fi

# ============ 7. 执行 ============
echo
echo "==> 整理索引"
for d in dist build bin obj .GC; do
  TRACKED=$(git ls-files -- "*$d/*" 2>/dev/null | head -1)
  if [ -n "$TRACKED" ]; then
    C=$(git ls-files -- "*$d/*" 2>/dev/null | wc -l | tr -d ' ')
    git rm -r --cached --quiet -- "*$d/*" 2>/dev/null || true
    git rm -r --cached --quiet -- "$d" 2>/dev/null || true
    echo "    - 移出 $C 个 $d/ 内的文件（本地保留）"
  fi
done

git add -A 2>/dev/null || true

STAGED=$(git diff --cached --name-only | wc -l | tr -d ' ')
if [ "$STAGED" = "0" ]; then
  echo "==> 没有可提交的改动"
else
  git commit -q -m "$MSG"
  echo "==> 已提交 $STAGED 个文件"
  git log --oneline -1 | sed 's/^/    /'
fi

FAILED=0
TOTAL=0
for R in $SELECTED; do
  TOTAL=$((TOTAL + 1))
  echo
  echo "==> [$TOTAL] 推送到 $R"
  if git push -u "$R" "$BRANCH" 2>&1 | sed 's/^/    /'; then
    echo "    OK"
  else
    echo "    !! 失败。若确认以本地为准："
    echo "       git push --force-with-lease $R $BRANCH"
    FAILED=$((FAILED + 1))
  fi
done

echo
if [ "$FAILED" -eq 0 ]; then
  echo "==> 完成：$TOTAL 个仓库已同步"
else
  echo "==> 完成，但有 $FAILED / $TOTAL 个仓库推送失败"
  exit 1
fi
