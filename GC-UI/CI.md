# GC-UI 持续集成

夜间 alpha 构建的工作流在 [`.gitea/workflows/gc-ui-alpha.yml`](../.gitea/workflows/gc-ui-alpha.yml)。
本仓库是自建 Gitea（不是 GitHub），所以目录是 `.gitea/workflows/`，语法与 GitHub Actions 基本一致，
差异集中在 **runner 标签** 和 **API 前缀** 两处。

## 它做什么

| 任务 | 运行环境 | 产出 |
|---|---|---|
| `prepare` | `ubuntu-latest` | 算版本号，建一个 **草稿 + prerelease** 的 Release |
| `windows` | `windows-latest` | `gc-ui-alpha-windows-x64.zip`（Flutter release 目录） |
| `macos` | `macos-latest` | `gc-ui-alpha-macos-<arch>.zip`（`gc_ui.app`，用 `ditto` 打包保真） |
| `publish` | `ubuntu-latest` | 两个平台都成功后才把草稿翻成可见，并断言 latest 没被占用 |

- **定时**：每天 **21:00（Asia/Shanghai）**，cron 写作 `TZ=Asia/Shanghai 0 21 * * *`。
  Gitea 1.23 起 Actions 的 cron 默认按 **UTC** 解释，所以这里显式带时区前缀；
  若你的实例不认 `TZ=`，改成等效的 UTC 写法 `0 13 * * *`。
- **alpha 不占用 latest**：Gitea 的 `/releases/latest` 只返回「最近一个既非草稿、也非 prerelease」
  的发布，所以 `prerelease: true` 的夜间版本永远不会成为 latest。`publish` 任务最后会再查一次
  `/releases/latest`，一旦指向本次 tag 就 fail。
- **手动触发**：Actions 页面 Run workflow；或

  ```bash
  curl -X POST -u "USER:TOKEN" \
    "https://git.xhmcl.com/api/v1/repos/Morris/GC/actions/workflows/gc-ui-alpha.yml/dispatches" \
    -H 'Content-Type: application/json' -d '{"ref":"gc-ui","inputs":{"note":"手动验证"}}'
  ```

## 前置条件（缺一不可）

### 1. 必须有 act_runner，并且带这三个标签

```bash
# 在准备当构建机的机器上（Windows / macOS 各一台，或一台 Linux 只跑 prepare/publish）
docker run -d --name gitea-runner \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /path/to/runner-data:/data \
  -e GITEA_INSTANCE_URL=https://git.xhmcl.com \
  -e GITEA_RUNNER_REGISTRATION_TOKEN=<在 站点管理 → Actions → Runners 里拿> \
  -e GITEA_RUNNER_LABELS=ubuntu-latest:docker://gitea/runner-images:ubuntu-latest,windows-latest:docker://<你的 windows 镜像>,macos-latest:docker://<你的 macos 镜像> \
  gitea/act_runner:latest
```

关键点：**`runs-on` 的值必须和 runner 上注册的 label 完全一致**，否则任务永远排队。
Windows / macOS 的构建**不能**跑在 Linux docker 容器里（一个要 MSVC，一个要 Xcode），
所以那两个 label 需要指向真实的 Windows / macOS 机器（自托管 runner）。

### 2. Flutter SDK

工作流里有一条「准备 Flutter SDK」：机器上已有 `flutter` 就直接用；没有就按
`FLUTTER_VERSION`（默认 3.24.5，需 >= 3.5.4 以匹配 `pubspec.yaml` 的 `sdk: ^3.5.4`）从
`storage.googleapis.com` 下载官方 SDK 并写进 `PATH`。所以机器只要能上外网就够，不必预装。
网络受限时请预装 Flutter 并确保 `flutter` 在 `PATH` 里。

Windows 还需要 **VS Build Tools 的「使用 C++ 的桌面开发」工作负载**；macOS 需要 **Xcode**。

### 3. Token

优先用仓库 Secrets 里的 `RELEASE_TOKEN`（Gitea 设置 → Actions → Secrets，需 `repo` 权限的个人访问令牌）；
没配就退回 Gitea 自动注入的 `github.token`。若自动 token 没有 release 权限，
创建 Release 那步会报 401/403 —— 这时配一个 `RELEASE_TOKEN` 即可，不用改工作流文件。

## 本地构建（不经 CI）

```bash
# macOS
./GC-UI/build-ui.sh macos

# Windows
GC-UI\build-ui.cmd
```
