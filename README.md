# GC

天文教学模型**启动器**：管理/启动模型、启动时自动检查并下载最新版模型与启动器、设置语言与内存。

> **分层约定（重要）**
> - `src/` = 启动器：扫描、下载、开窗口、显示加载进度、自检更新。**不渲染、不含任何模型内容、发布包不内置模型**。
> - `model/` = 模型的一切：**源码、编译后的 dll、场景数据、封面、语言包、共享引擎、契约**，独立分发、独立版本。
> - 模型相关代码全部直接存在模型目录里，仓库根没有 `model-src/` / `model-sdk/` 这类旁路目录。
> - 想加新内容 = 在 `model/<id>/` 里放模型并推送仓库，`src` 一行都不用改，客户端启动自动更新。

- UI：C# + .NET 8（可切 .NET 10）+ Avalonia，跨平台
- 模型加载：`AssemblyLoadContext` 加载模型自带 dll，契约与 Avalonia 走默认上下文，关窗即卸载
- 目标平台：**macOS arm64 / Windows x64 / Windows arm64**，一条命令全部编译
- 更新仓库：<https://gitcode.com/2509_92447106/GC>，模型目录 <https://gitcode.com/2509_92447106/GC/tree/main/model>

## 运行目录（发布包里就长这样）

```
<安装目录>/
  GC.exe | GC.app/
  .GC/
    settings.json        设置 + 已安装版本记录
    model/               启动时下载的模型
    cache/               下载暂存 / 原子替换
    update/              待下次启动应用的启动器更新
```

**发布包不含任何模型**。首次启动联网检查后把模型下载到 `.GC/model/` 并立刻显示；
安装目录不可写时自动退回用户数据目录。`.GC/` 可以随程序一起搬迁，也可以整个删掉重新下载。

## 自动更新

| 检查项 | 不是最新版时 | 生效时机 |
| --- | --- | --- |
| 模型 | 从 gitcode 下载到 `cache/`，校验后原子替换 `.GC/model/<id>` | **下载后立即应用**（本次启动首页就刷新） |
| 启动器 | 下载对应平台 zip 到 `.GC/update/payload`，写 `pending.json` | **下次启动时应用**（独立脚本覆盖程序并重启） |

- 默认更新仓库写在 `.GC/settings.json` 的 `updateRepository`：
  `https://gitcode.com/2509_92447106/GC`（也可换成自己的仓库或 `.../tree/main/model` 地址）
- 版本来自仓库根 `version.json`（由 `./build.sh stamp` 生成）：模型版本 = **模型包内容指纹**，
  所以**改了模型文件，版本号自动变**；启动器版本 = 构建版本。
- `settings.json` 记录 `installedModelVersions` / `pendingLauncherVersion` / `lastUpdateCheck`。
- 断网、仓库还没推 `version.json`、下载失败：**一律保留本地模型，绝不把模型库清空**，底部状态条说明原因。
- 设置页可关闭自动更新；状态条可手动「检查更新」（忽略开关强制检查）。
- 离线教室：把仓库（含 `version.json` + `model/`）拷到 U 盘，在 `settings.json` 填
  `updateLocalRoot` 指向它，即可完全离线更新。
- 发新版：把 `dist/GC-<rid>.zip` 上传到 Release / 仓库 `dist/`，把直链填进 `version.json`
  对应平台的 `url`（`./build.sh publish` 会生成 zip 并自动写入 `sha256` / `size`）。
  更新包只含程序文件，**不会覆盖 `.GC/` 里的模型与设置**。

## 目录结构

```
Repository/
├── GC.sln
├── Directory.Build.props            # 公共构建属性
├── version.json                     # ★ 自动生成的版本清单（推送到仓库后才生效）
├── build.sh / build.ps1             # 构建脚本
├── GC/                              # 启动器主要代码
│   └── src/
│       ├── GC.Core.csproj
│       ├── Models/  Services/       # 清单/设置/目录探测/i18n/模型库/下载/自动更新
│       ├── ViewModels/  Views/      # 主页、下载、设置和启动更新状态
│       ├── Assets/i18n/             # 启动器界面语言包
│       └── test/dotnet/GC.Tests/    # 离屏校验 + --update-test 离线更新链路验证
├── GC-BOOT/                         # 轻量启动入口
│   ├── GC-BOOT.csproj
│   ├── Program.cs
│   └── app.manifest
├── GC-DRAW/PlayerWindow.cs          # 通用模型窗口宿主；实际绘制由模型提供
├── model/                           # ★ 模型的一切
│   ├── version.json                 #   与根 version.json 相同（便于按 model/ 地址读取）
│   ├── _sdk/GC.ModelSdk/
│   ├── _engine/
│   │   ├── src/                     #     模型专用引擎与渲染，留在 model 内
│   │   ├── PreviewGen/              #     用模型渲染器生成封面
│   │   └── VersionStamp/            #     按内容指纹自动打版本、生成 version.json
│   ├── earth-rotation/
│   │   ├── earth-rotation.json
│   │   ├── src/                     #   本模型源码
│   │   └── earth-rotation.model/    #   dll + scene.json/land.json + preview.png + i18n
│   └── earth-revolution/
└── dist/                            # 发布产物（只有启动器 + .GC 骨架）
```

模型库位置：`EMS_MODELS` 覆盖 → 开发态仓库根 `model/` → **发布态 `<可执行文件>/.GC/model/`**。

## 构建与运行

```bash
./build.sh build           # 编译整个解决方案
./build.sh models          # 编译模型 dll + 生成封面 + 自动打版本
./build.sh stamp           # 只重新生成 version.json 与模型版本
./build.sh run             # 运行
./build.sh test            # 离屏校验：页面 + 真实加载模型窗口截图
./build.sh update-test     # 离线验证自动更新链路（不联网、不替换程序）
./build.sh publish         # 三平台发布（不内置模型）+ 建 .GC 骨架 + 生成更新包
./build.sh pack-models     # 单独打包模型 -> dist/models-<version>.zip
./build.sh pack-launcher   # 生成三个平台的启动器更新包 zip
./build.sh clean
```

Windows 用 `.\build.ps1`（参数相同）。

| 产物 | 平台 |
| --- | --- |
| `dist/mac-arm64/GC.app` | macOS Apple Silicon |
| `dist/win-x64/GC.exe` | Windows x64 |
| `dist/win-arm64/GC.exe` | Windows on ARM |

均为自包含单文件启动器，**发布包内不含模型**。改完模型：
`./build.sh models` → 把 `version.json` 和 `model/` 推送到 gitcode → 客户端下次启动自动更新。

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

启动器先弹进度窗，在后台调用 `Prepare`（模型用 `ReportProgress` 汇报真实进度），
再在 UI 线程调用 `CreateView`，把返回的控件直接铺满窗口 —— 除此之外什么都不做。

详细清单字段、数据格式、封面生成见 [`model/README.md`](model/README.md)。

## 模型窗口

- 启动模型 → **带进度条的加载窗**（读取数据 → 生成地表纹理 → 构建界面，真实进度）→ 新开独立窗口
- 主界面保持原页面，可同时开多个模型窗口；关窗即卸载模型程序集
- 启动器快捷键 `Esc` 关闭、`F11` 全屏；模型窗口内的快捷键由模型自己定义

## 地球模型（model/_engine + model/<id>/src）

- **地表**：加载时烘焙 1024×512 等经纬度纹理 —— 海洋深度、沙漠/雨林/针叶林/苔原分带、极地冰盖、
  山脉起伏（高度梯度参与光照）、云层、夜间城市辉光；每帧只做双线性采样 + 地形光照，60 FPS
- **教材数值**：黄赤交角 / 回归线统一为 **23.5°**，极圈 66.5°；夏至直射 **23.5°N**、冬至 **23.5°S**、
  春分秋分 **0°（赤道）**。球面上画出赤道、南北回归线、南北极圈与真实日下点黄圈，读数不受「夸张倾角」影响
- **自转**：球面像素渲染、晨昏线、大气边缘光、经纬网、地轴、太阳直射点；只有自转视角，
  **没有节气定位**（节气属于公转）
- **公转**：太阳—轨道—地球全景、地轴方向空间固定、二分二至标记与一键定格、HUD（角度 / 直射纬度 / 日期）
- **触控一体机**：双指捏合缩放（跟手）、滚轮缩放、画布左下角大号 ＋ / − / 复位按钮；
  所有工具栏按钮 ≥ 40px。单指或鼠标拖动可直接转动地球，**拖动时画面定格但视角俯仰与轨道平面始终不变**，
  俯视角只由「俯视」按钮显式切换
- **课堂演示**：点二分二至自动暂停定格并显示节气、教材日期、直射纬度与现象说明；
  右侧课堂卡含可放大观察到的「地球特写」；`◀ ▶` 步进 5°、教学要点点击自动定格定位；
  快捷键 `空格` 播放/暂停、`← →` 步进、`1–4` 二分二至、`F` 全屏、`+ -` 缩放、`0` 复位视角
- 节气日期、直射纬度、现象说明、教学要点全部写在 `scene.json` 里；夸张倾角只改示意图形，不改教学读数

## 模型源（手动导入）

除了启动时自动更新，「下载」页还支持手动导入普通 git 仓库：
**仓库根目录下的 `model/` 文件夹即模型列表**。支持 GitHub / GitLab / Gitee（zip 归档，
失败回退 `git clone --depth 1`）、zip 直链、本地目录。导入后缓存在 `数据目录/cache/sources/<id>`。

## 启动时运行内存分配

设置页的「启动时运行内存分配」类似启动器的 -Xmx：启动器把这份预算通过 `IModelContext.MemoryBudgetMb`
交给模型，具体怎么用（掩膜分辨率、缓存）由模型自己决定。
