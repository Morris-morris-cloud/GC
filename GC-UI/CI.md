# GC-UI 持续集成

alpha 构建的工作流在 [`.gitea/workflows/gc-ui-alpha.yml`](../.gitea/workflows/gc-ui-alpha.yml)，
**只支持手动触发**（定时任务已取消：它绕开 build.sh，产出的包没有后端、没有前端，跑起来只是个空壳；
另外 `cron` 里的 `TZ=` 前缀在部分 Gitea 版本上是非法表达式）。
PR 审核流水线在 [`.gitea/workflows/pr-check.yml`](../.gitea/workflows/pr-check.yml)。
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
# 按构建主机自动选目标，每条产物都带 Flutter 前端：
#   macOS 主机 → mac-arm64（原生）+ win-amd64 / win-arm64（mingw 交叉）
#   Linux 主机 → win-amd64 / win-arm64（mingw 交叉）
#                Linux 产物默认不出：需 aarch64 主机 + GC_BUILD_LINUX=1
#   Windows 主机 → win-amd64 / win-arm64（原生）
./build.sh build

# 前端是必需项：flutter 缺失或编译失败会直接中断，
# 确需降级成只有 Avalonia 回退界面的包时才用：
./build.sh build --allow-no-ui

# 只编 macOS 前端
./GC-UI/build-ui.sh macos

# Windows 主机原生构建
GC-UI\build-ui.cmd
```

### Linux 前端（原生）

Flutter 的 Linux 桌面端只能在 Linux 上编，需要 GTK3 开发库与 C++ 工具链：

```bash
sudo apt install clang cmake ninja-build pkg-config libgtk-3-dev libstdc++-14-dev
```

`libstdc++-14-dev` 容易漏：clang 18 会去 `/usr/lib/gcc/x86_64-linux-gnu/14/` 找
`-lstdc++`，系统只装了 gcc-13 的开发包时链接会失败，报 `cannot find -lstdc++`。

产物布局：`GC-UI` + `lib/libflutter_linux_gtk.so` + `data/`，与官方 `flutter build
linux` 的 bundle 一致，直接铺进 `dist/linux-arm64/`。

### Windows 前端交叉编译

Flutter 官方不支持在非 Windows 主机上 `flutter build windows`，但 Windows 嵌入层
（`windows/runner/*.cpp` + `cpp_client_wrapper`）是纯 Win32 + 标准 C++，只通过 C ABI
调用 `flutter_windows.dll`，所以 mingw-w64 就能编译。整条链路上唯一必须执行的
Windows 二进制是 AOT 快照生成器 `gen_snapshot.exe`，交给 wine 跑即可。

```bash
# 依赖
sudo apt install mingw-w64 cmake ninja-build wine          # Linux
brew install mingw-w64 cmake ninja                          # macOS
brew install --cask wine-stable                             # macOS 的 wine

# 单独构建（默认输出到 GC-UI/frontend/build/windows/x64/runner/Release）
./buildsh/cross/build-ui-win-cross.sh [输出目录]
```

产物与官方 toolchain 完全一致，可直接拷到 Windows 运行：

```
GC-UI.exe   flutter_windows.dll   data/{icudtl.dat, app.so, flutter_assets/}
```

实现分三层：

| 文件 | 职责 |
|---|---|
| `buildsh/cross/toolchain-mingw.cmake` | CMake 交叉工具链（posix 线程模型，静态链接 libstdc++，产物只依赖系统 DLL） |
| `buildsh/cross/CMakeLists.txt` | 只编 C++ 外壳；绕开 `tool_backend.bat` 与 MSVC 专用参数 |
| `buildsh/cross/build-ui-win-cross.sh` | 拉引擎产物、装 `gen_snapshot` 的 wine 包装、生成 AOT 快照、组装 |

两个容易踩的坑，脚本里已处理：

- `flutter_tools` 会 exec 路径为 `.../windows-x64-release/gen_snapshot`（**没有 `.exe`**）
  的文件，所以脚本在原 `.exe` 旁边放一个同名 shell 脚本转调 wine。
- `Runner.rc` 在 windres 下有两处不兼容：图标用反斜杠相对路径、`VERSION_AS_STRING`
  依赖命令行传 `-D` 的转义字符串。脚本生成一份路径与宏都已规整的 rc 副本再编译。

### 交叉编译必须自己嵌 DPI 清单

`windows/runner/CMakeLists.txt` 把 `runner.exe.manifest` 当**源文件**列进 target，
Visual Studio 生成器会把它交给链接器（`/MANIFESTINPUT`）自动嵌入 exe；
但 `buildsh/cross/CMakeLists.txt` 只用 windres 编 `Runner.rc`，CMake 不会做这一步。
结果就是交叉编出来的 `GC-UI.exe` **完全没有清单**：Windows 认为它「DPI 不感知」，
在 125% / 150% 缩放的屏幕上把整块窗口当位图拉伸 —— 界面整体发虚，中文小字最明显。

现在两处都已加固，改这块代码时不要拆掉任何一处：

| 位置 | 做法 |
|---|---|
| `buildsh/cross/CMakeLists.txt` | 往 `Runner.cross.rc` 追加 `1 24 "<abs>/runner.exe.manifest"`（id 1 = CREATEPROCESS_MANIFEST_RESOURCE_ID，type 24 = RT_MANIFEST） |
| `windows/runner/main.cpp` | 建窗之前调 `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`，失败依次退到 `SetProcessDpiAwareness` / `SetProcessDPIAware` |

`main.cpp` 那层是兜底：即使清单因为工具链原因没嵌进去，程序自己也会声明 DPI 感知。
验证办法（Windows）：`GC-UI.exe` 属性 →「兼容性」里不该出现「替代高 DPI 缩放行为」，
或在 125% 缩放下截图，文字边缘应是锐利的 1.25 倍像素，而不是模糊的插值。

`.gitea/workflows` 里的 `windows` 任务仍然跑在 `windows-latest` 上；若手头只有 macOS/Linux
构建机，可以把该任务的 `runs-on` 换成对应标签，直接复用这套交叉编译脚本，
不再需要 VS Build Tools。
