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

## autogit —— 交互式提交（支持多仓库）

```bash
./git.sh autogit
```

交互顺序：

**① 选择仓库（可多选）**

```
已配置的仓库：
  1) origin     git@gitcode.com:2509_92447106/GC.git
  2) gitee      git@github.com:user/GC.git

  a) 全部
  n) 添加新仓库地址

请选择（可多选，用空格或逗号分隔，如 1 3  或  a  或  n；默认 1）：
```

支持的输入：

| 输入 | 含义 |
| --- | --- |
| `1` | 第 1 个仓库 |
| `1 3` / `1,3` / `1、3` | 同时选第 1 和第 3 个 |
| `a` | 全部仓库 |
| `n` | 添加新地址（**可连续输入多个，空行结束**） |

选 `n` 时可以一次加好几个：

```
可以连续输入多个地址，每行一个，输入空行结束：
  地址： git@gitcode.com:user/GC.git
  给这个仓库起个名字（回车自动命名）： origin
    已添加 origin -> git@gitcode.com:user/GC.git
  地址： git@github.com:user/GC-mirror.git
  给这个仓库起个名字（回车自动命名）： mirror
    已添加 mirror -> git@github.com:user/GC-mirror.git
  地址：                              ← 空行结束
```

**② 确认仓库地址**

```
 目标仓库（共 2 个）：
   origin     git@gitcode.com:2509_92447106/GC.git
   mirror     git@github.com:user/GC-mirror.git
----------------------------------------------
以上地址都正确吗？(Y/n)
```

答 `n` 会逐个询问新地址，直接回车表示不改。

**③ 确认分支**

```
提交分支（当前 main，直接回车沿用，或输入其它分支名）：
----------------------------------------------
 提交分支：main
----------------------------------------------
分支正确吗？(Y/n)
```

输入其它名字时，不存在会自动创建并切换。

**④ 输入提交信息**

```
请输入提交信息：修复渲染卡顿
```

留空则自动生成 `更新（日期 时间）`。

**⑤ 最终确认**

```
==============================================
 分支：    main
 信息：    修复渲染卡顿
 推送到：
   - origin     git@gitcode.com:2509_92447106/GC.git
   - mirror     git@github.com:user/GC-mirror.git
==============================================
确认提交并推送？(y/N)
```

确认后逐个推送，最后汇总：

```
==> [1] 推送到 origin
    OK
==> [2] 推送到 mirror
    OK

==> 完成：2 个仓库已同步
```

参数：

```bash
./git.sh autogit -m "修复渲染卡顿"   # 跳过信息输入
./git.sh autogit --dry-run          # 只看不动
./git.sh autogit -y -m "信息"        # 全自动
```

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
