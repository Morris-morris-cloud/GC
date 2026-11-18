# GC

天文教学模型**启动器**：管理/启动模型、启动时自动检查并下载最新版模型与启动器、设置语言与内存。

> **分层约定（重要）**
> - `GC/` = 启动器：扫描、下载、开窗口、显示加载进度。**不渲染、不含任何模型内容、发布包不内置模型**。
> - `model/` = 模型的一切：**源码、编译后的 dll、场景数据、封面、语言包、共享引擎、契约**，独立分发、独立版本。
> - 模型相关代码全部直接存在模型目录里，仓库根没有 `model-src/` / `model-sdk/` 这类旁路目录。
> - 想加新内容 = 在 `model/<id>/` 里放模型并推送仓库，启动器一行都不用改，客户端启动自动更新。
> - `GC-SAVEDOG/` = 更新守护进程：验签、原子替换、启动后健康检查、失败回滚。

- UI：C# + .NET 8（可切 .NET 10）+ Avalonia，跨平台
- 模型加载：`AssemblyLoadContext` 加载模型自带 dll，契约与 Avalonia 走默认上下文，关窗即卸载
- 目标平台：**Windows x64 / Windows arm64 / macOS arm64 / Linux x64**
- 更新仓库：<https://git.xhmcl.com/Morris/GC>，模型目录 <https://git.xhmcl.com/Morris/GC/src/branch/main/model>

## GC-UI：前后端分离版（Flutter 前端）

`GC-UI/` 是同一套服务层的**前后端分离**实现，界面改用 Flutter，Windows 与 macOS 共用一套代码：

```
GC-UI/
├── backend/GC.UI.Server/   .NET 8 本地 HTTP 服务（复用 GC/src + GC-DOWNLOAD）+ 模型窗口宿主
└── frontend/               Flutter 桌面应用（windows/ 与 macos/ 原生 runner）
```

- **前端**：Flutter（`flutter create --platforms=windows,macos` 生成两个原生壳），负责首页 / 下载 / 设置 / 关于
- **后端**：.NET 8 最小 API，只监听 `127.0.0.1`，负责模型扫描、下载、更新、设置持久化
- **模型窗口**：仍由 .NET 承载 —— 模型 dll 返回的是 Avalonia `Control`，渲染栈属于模型契约，不能换

`GC-UI/` 的详情、平台选型理由与接口列表见 [`GC-UI/README.md`](GC-UI/README.md)。
「关于」页会直接展示 **源码语言：C#、前端：Flutter**（由后端 `/api/about` 下发）。

```bash
# 后端
dotnet run --project GC-UI/backend/GC.UI.Server

# 前端（另开终端）
cd GC-UI/frontend && flutter pub get
flutter run -d windows     # 或 -d macos
```

详见 [GC-UI/README.md](GC-UI/README.md)（含完整接口表、构建脚本与测试说明）。


## 快速开始

```bash
# 依赖：.NET 8 SDK（macOS 另需 Xcode Command Line Tools，用于 .icns / ditto）
dotnet --version                # 8.0.x
./build.sh build                # 三平台完整发布 -> dist/
./build.sh run                  # 开发态直接运行（读仓库 model/，跳过在线替换）
./build.sh check                # 编译 + 自检 + 离屏渲染 + 产物检查
```

Windows 用 `.\build.ps1`，参数相同（`build / check / models / stamp / run / test / update-test / pack-models / clean / doctor`）。
`build.sh test` 的离屏渲染需要图形栈：Linux CI 上用 `xvfb-run ./build.sh test`。

## 运行目录（发布包里就长这样）

```
<解压出来的目录>/                    ← 压缩包里只有这一个文件夹
  GCL-<版本>-<平台>(.exe | .app/)  启动器
  GC-UI(.exe)                       Flutter 前端
  safedog/GC-SAVEDOG[.exe]          更新守护进程（运行时复制到 .GC/safedog）
  data/ lib/ *.dll                  后端与 Flutter 运行时
  .GC/                              首次启动自动生成
    config/settings.json            用户设置（与缓存分开：删 cache 不会丢配置）
    model/                          启动时下载的模型
    cache/                          下载暂存 / 原子替换
    update/                         待应用的启动器更新（apply-request.json + package.zip）
    safedog/GC-SAVEDOG[.exe]        实际运行的守护进程（不随整目录替换被覆盖）
    logs/gc-YYYYMMDD.log            日志（中文）
    logs/gc-YYYYMMDD.en.log         日志（English）
```

压缩包内只有一个顶层目录，解压后不会散落一屏 dll；主程序、后端、前端、守护进程都在
这个目录里。`.GC` 紧挨着主程序，装在哪都便携 —— 整个目录复制走，模型、设置、日志跟着走。

**发布包不含任何模型**。首次启动联网检查后把模型下载到 `.GC/model/` 并立刻显示；
安装目录不可写时自动退回用户数据目录。`.GC/cache` 与 `.GC/model` 随时可删，程序会重新下载；
`.GC/config/settings.json` 删掉等于恢复出厂设置。

## 自动更新

| 检查项 | 不是最新版时 | 生效时机 |
| --- | --- | --- |
| 模型 | 按文件清单下载到 `cache/`，校验哈希后原子替换 `.GC/model/<id>` | **下载后立即应用**（本次启动首页就刷新） |
| 启动器 | 下载对应平台 zip 到 `.GC/update/`，交给 `GC-SAVEDOG` | **下次启动时应用**（重启后自动替换） |

启动器更新的完整流程：

```
启动器下载 zip -> 校验 sha256 -> 写 apply-request.json -> 启动 .GC/safedog/GC-SAVEDOG -> 自己退出
  GC-SAVEDOG: 二次验签 -> 检查 zip 安全性 -> 解压到 .GC/update/stage
            -> 整目录替换：旧文件搬到 .GC/update/backup，新文件就位（保留 .GC 不动）
            -> 启动新版，等 .GC/update/health.json
            -> 收到：删备份、写 last-good.json
            -> 没收到/进程退出：撤掉新文件、把备份搬回、重启旧版，写 state.json = rolledback
```

发布包是**整目录**（启动器 + 后端运行时 + Flutter 前端资源），所以更新必须整目录替换：
只替换一个 exe 会留下旧后端 / 旧界面，新前端调不通接口。替换只动程序文件，
`.GC`（模型库、设置、日志、更新目录）和正在运行的 `GC-SAVEDOG.exe` 一律跳过。

也就是说：**新版本起不来，用户不会被卡死**。找不到 `GC-SAVEDOG` 的旧安装会退回脚本流程（过渡用）。

- 默认更新仓库写在 `.GC/config/settings.json` 的 `updateRepository`：
  `https://git.xhmcl.com/Morris/GC`（也可换成自己的仓库或 `.../src/branch/main/model` 地址）
- 断网、仓库没推 `version.json`、下载失败：**一律保留本地模型，绝不清空模型库**，底部状态条说明原因
- 首次启动断网且本地无模型：状态条会明确提示「去下载页重试 / 手动放入 `.GC/model/`」，不会白屏干等
- 离线教室：把仓库（含 `version.json` + `model/`）拷到 U 盘，在 `settings.json` 填
  `updateLocalRoot` 指向它，即可完全离线更新（同样会验签）

## 安全模型

威胁模型很直接：**模型 dll 会被 `AssemblyLoadContext` 直接执行**，启动器更新包会替换可执行文件。
只要仓库被入侵，攻击者就能让所有客户端跑他的代码。因此信任链是：

```
内置公钥（源码）
  └─ 验证 version.json 的 ECDSA P-256 签名
       ├─ 得到启动器包 sha256 → 校验下载到的 zip
       │    └─ GC-SAVEDOG 再次验签 + 复算 sha256 + 检查 zip 结构 → 原子替换
       └─ 得到每个模型的 manifestSha256 / filesSha256
            └─ 下载后复算，比对通过才安装
                 └─ 加载 dll 前抽查 dll / json 哈希（.integrity.json）
```

要点：

| 机制 | 防的是什么 | 代码位置 |
| --- | --- | --- |
| 清单签名（ECDSA P-256 / SHA-256） | 仓库被改、清单被替换 | `GC/src/Security/ManifestSignature.cs` |
| 规范化 JSON（键排序、去 signature） | 「同内容不同字节」导致签名不可复现 | `GC/src/Security/CanonicalJson.cs` |
| 包 sha256（取自已签名清单） | 下载被替换 / 传输损坏 | `GC/src/Security/ContentHash.cs` |
| 模型 manifestSha256 / filesSha256 | 下载后、安装前被掉包 | `GC/src/Security/ModelIntegrity.cs` |
| 加载前抽查 dll 哈希 | 本地模型文件被替换 | `GC/src/Services/ModelHost.cs` |
| `SafeArchive`（条目数/体积/压缩比/符号链接/穿越） | zip 炸弹、zip slip、链接攻击 | `GC/src/Security/SafeArchive.cs` |
| `GC-SAVEDOG` 独立进程 | 覆盖失败、断电、新版本起不来 | `GC-SAVEDOG/Applier.cs` |
| 健康标记 + 回滚 | 新版启动即崩 | `GC/src/Security/StartupHealth.cs` |

默认策略：**清单没签名或签名不符一律拒绝更新**。
自建私有构建可临时放开：`settings.json` 里 `requireSignedManifest: false`，
或环境变量 `GC_ALLOW_UNSIGNED_MANIFEST=1`（只建议调试用）。

### 签名密钥

- 私钥：`.signing/gc-signing.pem`（**已在 `.gitignore`，永不入库**；请自行备份）
- 公钥：硬编码在 `GC/src/Security/SignatureKeyring.cs`
  - 当前生效：`gc-2026-12`，指纹 `ee6083be47a87907`
  - 已停用：`gc-2026-11`，指纹 `b41b1dd6259ea9ed`（私钥遗失，仅保留占位）
- 生成新密钥：`dotnet run --project GC-SAVEDOG -- keygen --key-id gc-YYYY-MM`
- 轮换：把新公钥追加进 `SignatureKeyring.Keys`（保留旧公钥一个版本周期），
  同时把 `GC_SIGNING_KEY_ID`（默认值在 `buildsh/lib/build_funcs.sh`）改成新 keyId，
  再重新签名清单
- 部署前核对两边公钥一致：
  `dotnet run --project GC-SAVEDOG -- verify --manifest version.json`

`build.sh` 在刷新清单后自动调用 `sign_manifest` 签名并同步 `model/version.json`；
`.signing/gc-signing.pem` 不存在时只告警，清单不带签名（客户端会拒绝更新）。

> 换钥匙的代价：老客户端只认识编译进去的那份公钥，所以**用新密钥签出的清单
> 在旧版本上会判成「签名无效」**。密钥一旦丢失，只能靠发一个新版本把新公钥带出去，
> 中间这段时间老客户端无法自动更新 —— 私钥务必备份两处以上。

### zip 解压加固

更新包和模型源 zip 一律当成不可信输入：拒绝 `../`、绝对路径、盘符、UNC、符号链接；
条目数 ≤ 20000、解压后 ≤ 2 GiB、压缩比异常（>200:1 且体积较大）直接判为 zip 炸弹。

## 版本与指纹

- **启动器版本**：`Directory.Build.props` 的 `<Version>`；`./build.sh build -version=1.0.8` 可覆盖，
  只影响启动器（程序集版本、关于页、`version.json` 的 `launcher.version`）。
- **模型版本**：模型清单里**作者手写**的 `version`（如 `1.6`），`./build.sh stamp` 不会改它。
  客户端按字符串比较是否变化，所以改模型内容后必须手动升版本号，否则用户不会更新。
- **`build` 字段**：模型内容的构建指纹，只用于溯源；`updatedAt` / `build` 都不参与指纹计算，
  保证「同一份内容重复构建得到同一个值」。
- **内容哈希算法**（构建端与客户端必须一致）：

```
fileHash = SHA256(文件字节)
dirHash  = SHA256( 按相对路径 ordinal 升序，对每个文件拼接 "<相对路径>\0<fileHash>\n" )
```

  相对路径统一 `/` 分隔，跳过 `.` 开头的文件、`bin/`、`obj/`、`*.user`；
  不含时间戳、权限位、文件顺序。实现见 `GC/src/Security/ContentHash.cs`，构建端由
  `VersionStamp` 直接链接同一份源码，不会出现两套算法。

## 目录结构

```
Repository/
├── GC.sln
├── Directory.Build.props            # 公共构建属性（版本号在这里）
├── version.json                     # ★ 自动生成并签名的版本清单（推送到仓库后才生效）
├── build.sh / build.ps1             # 构建脚本（实际实现在 buildsh/ 与 buildps1/）
├── GC/                              # 启动器
│   └── src/
│       ├── GC.Core.csproj
│       ├── Security/                # ★ 验签、哈希、安全解压、完整性、健康标记
│       ├── Models/  Services/
│       ├── ViewModels/  Views/
│       ├── Assets/i18n/             # 界面语言包
│       └── test/dotnet/GC.Tests/    # 离屏校验 + --update-test 离线更新链路验证
├── GC-BOOT/                         # 轻量启动入口
├── GC-DOWNLOAD/                     # 源仓库 / 模型 / 启动器下载更新
├── GC-DRAW/PlayerWindow.cs          # 模型窗口宿主；绘制由模型提供
├── GC-SAVEDOG/                      # ★ 更新守护进程（apply / verify / keygen / sign / status）
├── model/                           # ★ 模型的一切
│   ├── version.json                 #   与根 version.json 完全相同（同一份签名）
│   ├── _sdk/GC.ModelSdk/
│   ├── _engine/{src,PreviewGen,VersionStamp}/
│   ├── earth-rotation/{earth-rotation.json,src/,earth-rotation.model/}
│   └── earth-revolution/
├── dist/                            # 发布产物（.gitignore，只通过 Release 分发）
└── .signing/                        # 签名私钥（.gitignore，绝不入库）
```

模型库位置：`EMS_MODELS` 覆盖 → 开发态仓库根 `model/` → **发布态 `<可执行文件>/.GC/model/`**。

`dist/` 不进仓库：二进制放 Git 会让 clone 变慢，也会让「仓库被改」的爆炸半径变大。
发布有两种方式，`version.json` 里两条路都写清楚了：

| 方式 | 清单字段 | 说明 |
| --- | --- | --- |
| Release 附件（推荐） | `url` | `.../releases/download/<版本>/GCL-<版本>-<系统>-<架构>.zip` |
| 仓库编译产物 | `path` / `dir` / `name` | `dist/<系统>-<架构>/GCL-<版本>-<系统>-<架构>.zip` |

下载端按 `url` → `path` → `dist/<系统>-<架构>/` 目录扫描的顺序尝试，
**每个候选都必须与清单里的 `sha256` 一致**（`sha256` 由签名保护），
所以「多发一个地方」不会放宽校验，只是让「只提交产物、不发 Release」也能更新。
清单缺 `sha256` 的平台会被直接跳过，不再退化成「只校验非空」。

## 构建与运行

```bash
./build.sh build           # 三平台完整发布：启动器 + SAVEDOG + 运行骨架 + 更新包 ZIP
./build.sh check           # 编译、自检、离屏渲染及发布 ZIP 完整性检查
./build.sh models          # 编译模型 dll、生成封面、更新并签名版本清单
./build.sh stamp           # 只重新生成 + 签名 version.json
./build.sh run             # Debug 运行启动器
./build.sh test            # 离屏校验，截图写入 build/screenshots/
./build.sh update-test     # 离线验证自动更新链路（验签 / 立即生效 / 篡改拒绝 / 断网兜底）
./build.sh pack-models     # 编译模型并打包 dist/models-<版本>.zip
./build.sh pack-launcher   # 把已有平台发布目录打成启动器更新 ZIP
./build.sh doctor          # dotnet 环境信息
./build.sh dist            # 列出 dist/ 产物
./build.sh clean           # 清理 build/ 与 dist/
```

| 产物 | 平台 |
| --- | --- |
| `dist/mac-arm64/GCL-<版本>-mac-arm64.zip` | macOS Apple Silicon（单顶层 `GCL-….app`） |
| `dist/win-amd64/GCL-<版本>-win-amd64.zip` | Windows x64（单顶层 `GCL-…/`，含 Flutter 前端与 `safedog/`） |
| `dist/win-arm64/GCL-<版本>-win-arm64.zip` | Windows on ARM 同上 |
| `dist/linux-arm64/GCL-<版本>-linux-arm64.zip` | Linux ARM64 同上（启动器无扩展名，需在 aarch64 主机构建） |

平台目录就是「该平台的编译产物目录」：打包完成后只留最终 zip（原始 `.app` / `.exe`
会被挪到 `build/trash/`，下次构建时移出，不做递归删除）。

发新版：`./build.sh build` → 把 `dist/<平台>/GCL-<版本>-<平台>.zip`
上传到 Gitea Release（tag `v1.0.8`），**或者**直接提交/上传到仓库的
`dist/<平台>/` 目录 → 提交并推送 `version.json` / `model/version.json`。
两种都能被客户端识别，客户端下次启动就会看到更新。

## 模型契约

```csharp
public interface IModelFactory        // 模型入口
{
    int ApiVersion { get; }           // 目前为 1
    IModelRuntime Create();
}

public interface IModelRuntime : IDisposable
{
    void Prepare(IModelContext context);         // 后台线程：读数据、烘焙纹理、汇报进度
    Control CreateView(IModelContext context);   // UI 线程：模型自己做界面
}

public interface IModelContext
{
    string ModelId { get; }
    string ModelDirectory { get; }
    string DataDirectory { get; }
    string Language { get; }
    int MemoryBudgetMb { get; }
    void RequestClose();
    void ReportProgress(double value, string? message = null);
    void Log(string message);
}
```

**线程约定**

| 成员 | 允许的线程 | 说明 |
| --- | --- | --- |
| `Prepare` | 后台线程（调用一次） | 可耗时；用 `ReportProgress` 汇报真实进度；此时**不要**创建控件 |
| `CreateView` | UI 线程（`Prepare` 之后调用一次） | 只在这里创建/操作 Avalonia 控件 |
| `ReportProgress` / `Log` | 任意线程 | 线程安全，内部自行封送到 UI |
| `RequestClose` | 任意线程 | 线程安全，等价于点窗口关闭按钮 |
| `Dispose` | UI 线程 | 释放后不要再调用 `Log` / `ReportProgress` |

**契约版本协商**

- `IModelFactory.ApiVersion` 与启动器支持的版本（当前 `1`）必须相等，否则拒绝加载并提示更新启动器。
- 模型清单可写 `minAppVersion`（如 `"1.0.8"`）：启动器版本更低时直接拒绝，并给出明确提示。
- 提升契约版本时必须保留旧接口的适配层，或在启动器里明确「最低启动器版本」，避免老客户端加载新模型后崩溃。

**卸载注意**：关窗即 `Unload()` 收集式 AssemblyLoadContext。只要有静态字段、未解绑的事件、
或缓存里还拿着模型内的类型/控件，程序集就回收不掉（内存持续增长）。
模型作者应避免静态持有 `Control` / `IModelContext`，并在 `Dispose` 里解绑事件。

详细清单字段、数据格式、封面生成见 [`model/README.md`](model/README.md)。

## 模型窗口

- 启动模型 → **带进度条的加载窗**（读取数据 → 生成地表纹理 → 构建界面，真实进度）→ 新开独立窗口
- 主界面保持原页面，可同时开多个模型窗口；关窗即卸载模型程序集
- 所有由启动器打开的窗口（模型窗、加载窗、确认框、更新窗）都是 16px 圆角

## 地球模型（model/_engine + model/<id>/src）

- **地表**：加载时烘焙 1024×512 等经纬度纹理 —— 海洋深度、沙漠/雨林/针叶林/苔原分带、极地冰盖、
  山脉起伏（高度梯度参与光照）、云层、夜间城市辉光；每帧只做双线性采样 + 地形光照，60 FPS
- **教材数值**：黄赤交角 / 回归线统一为 **23.5°**，极圈 66.5°；夏至直射 **23.5°N**、冬至 **23.5°S**、
  春分秋分 **0°（赤道）**。球面上画出赤道、南北回归线、南北极圈与真实日下点黄圈，读数不受「夸张倾角」影响
- **自转**：球面像素渲染、晨昏线、大气边缘光、经纬网、地轴、太阳直射点；只有自转视角，
  **没有节气定位**（节气属于公转）
- **公转**：太阳—轨道—地球全景、地轴方向空间固定、二分二至标记与一键定格、HUD（角度 / 直射纬度 / 日期）
- **触控一体机**：双指捏合缩放（跟手）、滚轮缩放、画布左下角大号 ＋ / − / 复位按钮；
  所有工具栏按钮 ≥ 40px。单指或鼠标拖动可直接转动地球，**拖动时画面定格但视角俯仰与轨道平面始终不变**
- **课堂演示**：点二分二至自动暂停定格并显示节气、教材日期、直射纬度与现象说明；
  右侧课堂卡含可放大观察到的「地球特写」；`◀ ▶` 步进 5°、教学要点点击自动定格定位；
  快捷键 `空格` 播放/暂停、`← →` 步进、`1–4` 二分二至、`F` 全屏、`+ -` 缩放、`0` 复位视角

## 启动时运行内存分配

设置页的「启动时运行内存分配」类似启动器的 `-Xmx`：启动器把这份预算通过 `IModelContext.MemoryBudgetMb`
交给模型，具体怎么用（掩膜分辨率、缓存）由模型自己决定。
