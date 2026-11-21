# GitHub Actions 说明

三个工作流，都在 `.github/workflows/` 下。自建 Gitea 里对应的老流水线在 `.gitea/workflows/`，两边互不影响。

| 工作流 | 触发 | 做什么 |
| --- | --- | --- |
| `nightly-prerelease.yml` | 每天 21:00（Asia/Shanghai），或手动 | 编译 macOS + Windows(amd64/arm64)，发布 **`nightly`** 预发布 |
| `build-bot.yml` | Issue / PR 评论里 `@build-bot build <目标>` | 只编译指定平台，发布 **`ip-prerelease`** 预发布 |
| `pr-review.yml` | 向 main 提 PR（新建 / 有新提交 / 重新打开） | 检查能否编译、是否有错、是否冲突、是否落后 main |

两个预发布用**不同的 tag**，互不覆盖：夜间构建发 `nightly`，手动触发的发 `ip-prerelease`。

## 每夜构建

时区用 `TZ=Asia/Shanghai`，cron 写成 `0 13 * * *`（= 21:00 UTC+8）。
产物是 `GCL-<版本>-<平台>.zip`，由 `build.sh` / `build.ps1` 生成在 `dist/<平台>/`。

`nightly` 这个 tag **每次滚动重建**（先删 release 和 tag，再按当前提交创建），
所以它的下载链接是稳定的。因为 tag 不是版本号，客户端的上传/更新逻辑会跳过它
——夜间版本永远不会被当成「有新版可用」推给用户，符合预期。

## build-bot 用法

在 Issue 或 PR 里评论：

```
@build-bot build mac        # 只编 macOS
@build-bot build win-amd    # 只编 Windows amd64
@build-bot build win-arm    # 只编 Windows arm64
@build-bot build all        # 三个平台全编
```

规则：

- 只有 `OWNER` / `MEMBER` / `COLLABORATOR` 的评论才会触发，其他人评论会被忽略。
- PR 上的评论编的是 **该 PR 的 head 提交**；Issue 上评论编的是 main 最新提交。
- 产物用 `gh release upload --clobber` 合并进 `ip-prerelease`，**不会**删掉别的平台资产。
- 编完会在评论里回帖：成功给出 release 链接，失败给出日志链接。
- 同一时刻只跑一个 build-bot（同一 concurrency 组），避免并发改同一个 release；
  `nightly` 用的是另一个组，两者互不干扰（tag 本来就不同）。

## PR 自动审查

对 main 的 PR 在新建 / 推新提交 / 重新打开时运行，检查四项：

1. **能否编译**：`dotnet build GC.sln -c Release`
2. **代码检查**：`flutter analyze`（在 `GC-UI/frontend`）
3. **是否冲突**：读 GitHub 的 `mergeable` 字段（异步计算，会重试几次）
4. **是否落后**：`git rev-list --count HEAD..origin/main`

结论写进一条带 `<!-- gc-pr-review -->` 标记的评论，**重复运行只更新这一条**，不会刷屏。
日志尾部（dotnet / flutter 各 40 行）折叠在评论的 `<details>` 里。

> 说明：这里只做「能编译 + 能通过静态分析」，不跑 `flutter build macos`
> （Ubuntu runner 没有 Xcode）。真正的三平台编译由 `nightly-prerelease` 和 `build-bot` 覆盖。

## 需要的仓库设置

- `Settings → Actions → General → Workflow permissions` 给 **Read and write**，
  否则 `gh release` / 评论接口会 403（工作流里已声明 `permissions:`，但组织策略可能覆盖）。
- Secrets 不需要额外配置，统一用内置的 `GITHUB_TOKEN`。
- macOS 产物是 **未签名** 的（`GC_NO_CODESIGN=1`）。下载后首次打开需右键 →「打开」，
  或在「系统设置 → 隐私与安全性」里放行。
