# git 工具（gitshell/）

仓库的 git 操作入口。与 `build.sh` / `build.ps1` 同样的设计：根目录只放一个薄入口，
实际脚本都在 `gitshell/` 里。

## 入口

| 平台 | 文件 |
| --- | --- |
| macOS / Linux | `./git.sh` |
| Windows | `git.bat` |

不带参数运行会列出所有可用命令。

```bash
./git.sh              # 显示帮助
./git.sh status       # 查看状态
./git.sh autogit      # 提交并推送
```

## autogit —— 一键多仓提交

提交当前改动，并推送到 **所有** 已配置的远程仓库。

```bash
./git.sh autogit                     # 自动生成提交信息
./git.sh autogit -m "修复渲染卡顿"     # 指定提交信息
./git.sh autogit -n                  # 只提交，不推送
./git.sh autogit --dry-run           # 只看会提交什么，不动仓库
```

### 多仓库

脚本遍历 `git remote` 里的每个远程依次 push：

```bash
# 查看现有远程
git remote -v

# 追加一个仓库（之后 autogit 会同时推它）
git remote add gitee git@github.com:user/GC.git
git remote add backup git@gitcode.com:user/GC-backup.git
```

推送失败时（通常是远程有本地没有的提交），脚本会提示：

```
git push --force-with-lease <remote> main
```

### 自动跳过

这些目录**永远不会被提交**（新增的由 `.gitignore` 挡住，历史上被跟踪的会先从索引移除，
本地文件保留）：

```
dist/   build/   bin/   obj/   .GC/   node_modules/
```

脚本按 `*bin/*`、`*obj/*` 这样的通配匹配**任意层级**，
所以 `model/earth-geography/src/bin/` 这类嵌套构建产物同样会被排除。

## status —— 状态速览

```bash
./git.sh status
```

显示当前分支、改动数量、最近提交，以及每个远程的领先/落后情况。

## 常见情况

**提交了但忘了排除构建产物**

```bash
git rm -r --cached -- '*bin/*' '*obj/*'
git commit -m "移除构建产物"
```

**远程有别人的提交，推不上去**

```bash
git pull --rebase origin main
./git.sh autogit
```

若确认以本地为准（会覆盖远程历史，慎用）：

```bash
git push --force-with-lease origin main
```

**想确认没有把不该提交的东西加进去**

```bash
./git.sh autogit --dry-run
```
