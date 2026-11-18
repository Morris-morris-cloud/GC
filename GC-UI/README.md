# GC-UI

GC 启动器的**前后端分离**实现：界面用 Flutter，服务层留在 .NET。

```
GC-UI/
├── backend/GC.UI.Server/     # .NET 8 后端：本地 HTTP API + 模型窗口宿主
│   ├── Program.cs            # 所有接口（最小 API）
│   ├── ApiModels.cs          # 前后端 JSON 契约
│   └── AvaloniaHost.cs       # 模型窗口宿主（Avalonia，惰性启动）
└── frontend/                 # Flutter 前端（Windows / macOS 桌面）
    ├── lib/
    │   ├── main.dart         # 应用入口
    │   ├── shell.dart        # 侧边栏 + 内容区 + 更新状态条
    │   ├── pages.dart        # 首页 / 下载 / 设置 / 关于
    │   ├── controller.dart   # 外壳状态（设置、健康、更新）
    │   ├── api_client.dart   # 后端 HTTP 客户端 + DTO
    │   ├── i18n.dart         # zh-CN / en 文案
    │   └── theme.dart        # 配色与通用组件
    ├── windows/              # Windows 原生 runner（flutter create 生成）
    ├── macos/                # macOS 原生 runner（flutter create 生成）
    └── test/                 # 单元测试 + 后端契约测试 + 截图脚本
```

## 为什么这样切

原来的 GC 是「Avalonia 界面 + 服务层」一体式应用。拆分时唯一不能动的是**模型契约**：
模型 dll 通过 `IModelRuntime.CreateView` 返回的是 Avalonia `Control`，渲染栈是契约的一部分。
把渲染换成 Flutter 等于让所有已发布模型失效。

所以切法是：

| 层 | 归属 | 说明 |
|---|---|---|
| 首页 / 下载 / 设置 / 关于 | **Flutter 前端** | 纯界面，通过 HTTP 调后端 |
| 模型扫描、下载、更新、设置持久化 | **.NET 后端** | 直接复用 `GC/src`（GC.Core）与 `GC-DOWNLOAD` |
| 模型自己的窗口 | **.NET 后端** | 模型返回 Avalonia 控件，由后端承载 |

前端只发一条「启动模型」的指令，模型窗口由后端在自己的 UI 线程上打开。

## 前端方案：为什么是 Flutter

Windows 与 macOS 用**同一套 Flutter 代码 + 各自的原生 runner**：

- **一套代码两个平台**：`flutter create --platforms=windows,macos` 生成 `windows/`（CMake + Win32 窗口）与 `macos/`（Xcode + Cocoa 窗口）两个原生壳，业务代码 100% 共用。
- **原生桌面窗口**：不是 WebView 套壳，两个平台都是真正的原生窗口，可设置最小尺寸、标题栏、DPI 缩放。
- **渲染一致**：Skia/Impeller 自绘，两个平台像素级一致，不需要为 WinUI / AppKit 各写一套控件。
- **热重载**：`flutter run -d windows` / `-d macos` 改界面即时生效，迭代比 XAML 快。
- **对比过的替代方案**：
  - *继续用 Avalonia*：跨平台没问题，但那就不是「前后端分离 + Flutter 前端」了。
  - *WinUI 3 + SwiftUI 各写一套*：两套 UI 代码、两套状态管理，维护成本翻倍。
  - *Electron / Tauri*：WebView 套壳，包体大（Electron ~150MB）或需要 Rust 工具链，且与 .NET 后端通信仍要额外桥接。

## 运行

### 1. 启动后端

发布版里后端不走独立进程：`GC-BOOT` 直接引用本工程，调用
`BackendServer.Start(args, port)` 在自己进程内托管，发布包因此只有一个 exe
（两个自包含进程会把 60MB 运行时复制两份）。下面的独立运行方式只用于开发调试。

```bash
# 仓库根目录
dotnet run --project GC-UI/backend/GC.UI.Server

# 指定端口（默认 17890）
dotnet run --project GC-UI/backend/GC.UI.Server -- --port 17890
# 或
GC_UI_PORT=17890 dotnet run --project GC-UI/backend/GC.UI.Server
```

端口约定集中在 `GC.Services.GcUiContract`（`DefaultPort` / `PortEnvVar`）：
正式启动时由 GC-BOOT 选端口（17890 被占用就往后试 9 个），再通过 `GC_UI_PORT`
环境变量同时传给后端和前端，所以**不需要为了换端口重新编译前端**。
启动后后端会在 `~/.GC/ui-backend.json` 写入 `{ port, pid, url, version, platform }`，
排查问题时可以据此确认实际端口。

### 2. 启动前端

```bash
cd GC-UI/frontend
flutter pub get

# Windows
flutter run -d windows

# macOS
flutter run -d macos

# 后端不在默认端口时
flutter run -d windows --dart-define=GC_UI_BASE_URL=http://127.0.0.1:18000
```

### 3. 打包

```bash
cd GC-UI/frontend
flutter build windows --release   # build/windows/x64/runner/Release/
flutter build macos --release     # build/macos/Build/Products/Release/
```

或直接用仓库根目录的脚本：

```bash
./GC-UI/build-ui.sh          # macOS / Linux 上构建（macOS 产物）
GC-UI\build-ui.cmd           # Windows 上构建（Windows 产物）
```

## HTTP 接口

后端只监听 `127.0.0.1`，不对外暴露。

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/health` | 健康检查 + 模型窗口宿主是否可用 |
| GET | `/api/models` | 已安装模型 |
| GET | `/api/models/{id}/preview` | 模型封面图（PNG） |
| POST | `/api/models/{id}/launch` | 启动模型（打开模型自己的窗口） |
| DELETE | `/api/models/{id}` | 删除模型 |
| GET | `/api/settings` | 读取设置 |
| POST | `/api/settings` | 修改设置（补丁式，只改传入字段） |
| GET | `/api/sources` | 模型源列表 |
| POST | `/api/sources/select` | 双源测速并切到最快 |
| GET | `/api/remote/models?sourceId=&force=` | 远端可下载模型 |
| POST | `/api/download` | 入队下载 `{ sourceId, modelId }` |
| GET | `/api/download/jobs` | 下载任务列表 |
| POST | `/api/download/{id}/cancel` | 取消 |
| POST | `/api/download/{id}/retry` | 重试 |
| POST | `/api/download/{id}/remove` | 移除任务 |
| POST | `/api/download/clear-finished` | 清除已完成 |
| POST | `/api/update/check` | 检查模型 / 启动器更新 |
| GET | `/api/about` | 版本 + 贡献者 |
| POST | `/api/shell/open-folder` | 打开目录（仅限启动器自己的目录） |
| POST | `/api/shell/open-url` | 打开 http/https 链接 |

契约定义见 `backend/GC.UI.Server/ApiModels.cs`，前端对应 `frontend/lib/api_client.dart`。

## 测试

```bash
# 后端编译
dotnet build GC-UI/backend/GC.UI.Server

# 前端静态检查 + 单元测试
cd GC-UI/frontend
flutter analyze
flutter test

# 前后端联调（需要后端正在运行；后端没起时自动跳过）
flutter test test/backend_contract_test.dart

# 生成界面截图到 test/shots/（视觉检查用）
flutter test tool/render_shots_test.dart
```

截图脚本覆盖四个页面（首页 / 下载 / 设置 / 关于）再加一整套外壳：
`test/shots/home.png`、`download.png`、`settings.png`、`about.png`、`shell.png`。
需要先在本机 17890 端口跑起后端；脚本用 `runAsync` 给真实 HTTP 留出时间，
否则 `flutter_test` 的假时钟会让页面停在加载态。

## 「关于」页展示的技术栈

「关于」页的版本卡片里直接写明这套实现的两个关键点：

| 行 | 值 | 来源 |
|---|---|---|
| 源码语言 | **C#** | 后端 `/api/about` 返回 `sourceLanguage` |
| 前端 | **Flutter** | 后端 `/api/about` 返回 `frontend` |
| 架构 | Flutter 前端 + .NET 后端（本地 HTTP） | 前端 i18n 文案 |

两个字段由后端下发（而不是前端写死），所以将来换实现时只改后端一处，
前端和「关于」页自动跟着变。契约字段见 `ApiModels.cs` 的 `AboutDto`，
前端解析在 `lib/api_client.dart` 的 `AboutInfo`。

## 安全边界

- 后端只绑定 `127.0.0.1`，不接受外部连接。
- `open-folder` 只允许打开模型库 / 数据目录 / 应用目录之内的路径，避免被当成任意路径执行器。
- `open-url` 只允许 `http` / `https`。
- CORS 只放行 `localhost` / `127.0.0.1` 来源，方便 `flutter run -d chrome` 调试界面。
