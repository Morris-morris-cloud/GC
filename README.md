# Earth Model Studio

天文教学模型**启动器**：主页管理/启动模型、从链接导入模型源并下载、设置模型源与启动内存分配。

> **分层约定（重要）**
> - `src/` = 启动器：扫描、下载、开窗口、显示启动进度。**不渲染、不含任何模型内容、不预置模型源**。
> - `model/` = 模型的一切：**源码、编译后的 dll、场景数据、封面、语言包、共享引擎、契约**，独立分发、独立版本。
>
> 结论：**模型相关代码全部直接存在模型目录里**，仓库根目录没有 `model-src/` / `model-sdk/` 这类旁路目录。
> 启动器只负责下载和启动；想加新内容 = 在 `model/<id>/` 里放模型，`src` 一行都不用改。

- UI：C# + .NET 8（可切 .NET 10）+ Avalonia，跨平台
- 模型加载：`AssemblyLoadContext` 加载模型自带 dll，契约与 Avalonia 走默认上下文，关窗即卸载
- 目标平台：**macOS arm64 / Windows x64 / Windows arm64**，一条命令全部编译

## 目录结构

```
EarthModelStudio/
├── EarthModelStudio.sln
├── Directory.Build.props            # 公共构建属性（-p:TargetFramework=net10.0 可切 .NET 10）
├── build.sh / build.ps1             # 构建脚本（build/run/test/publish/models/pack-models）
├── src/                             # ★ 启动器（没有渲染逻辑）
│   └── main/dotnet/EarthModelStudio/
│       ├── Models/                  #   模型清单、设置（只有启动器需要的元数据）
│       ├── Services/                #   目录探测/设置/i18n/模型库/模型源/下载/内存预算/**ModelHost**
│       ├── ViewModels/  Views/      #   主页、下载、设置、模型窗口壳
│       └── Assets/i18n/             #   启动器界面语言包
├── src/test/dotnet/…Tests/          # 离屏校验（启动器页面 + 真实加载模型窗口）
├── model/                           # ★ 模型的一切（源码 + 数据 + dll + 封面 + 语言包）
│   ├── _sdk/EarthModelStudio.ModelSdk/  #  模型契约（启动器与模型唯一的共享代码）
│   ├── _engine/                     #   共享引擎源码（渲染、界面、交互）
│   │   ├── src/                     #     CelestialView / PlanetTexture / EarthModelView …
│   │   └── PreviewGen/              #     用模型渲染器生成封面
│   ├── earth-rotation/
│   │   ├── earth-rotation.json      #   清单
│   │   ├── src/                     #   本模型源码（csproj + 入口工厂）
│   │   └── earth-rotation.model/    #   dll + scene.json/land.json + preview.png + i18n
│   └── earth-revolution/            #   同上结构，封面是公转场景
└── dist/                            # 发布产物（启动器 + 单独打包的模型）
```

模型库查找顺序：`EMS_MODELS` 环境变量 → 开发态仓库根 `model/` → 可执行文件旁 `model/` → 用户数据目录。

## 构建与运行

```bash
./build.sh build         # 编译启动器
./build.sh models        # 编译模型 dll + 生成封面（写进 model/）
./build.sh run           # 运行
./build.sh test          # 离屏校验：页面截图 + 真实加载模型窗口截图
./build.sh publish       # 一次性编译三个平台（启动器，不含模型）
./build.sh pack-models   # 编译模型后打包 -> dist/models-<version>.zip
./build.sh clean
```

Windows 用 `.\build.ps1`（参数相同）。

| 产物 | 平台 |
| --- | --- |
| `dist/mac-arm64/EarthModelStudio.app` | macOS Apple Silicon |
| `dist/win-x64/EarthModelStudio.exe` | Windows x64 |
| `dist/win-arm64/EarthModelStudio.exe` | Windows on ARM |

均为自包含单文件启动器，`model/` 目录为空（只有 README）。模型用 `pack-models` 单独分发。

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
- 快捷键 `Esc` 关闭、`F11` 全屏（其余快捷键由模型自己决定）

## 地球模型（model/_engine + model/<id>/src）

- **地表**：加载时烘焙 1024×512 等经纬度纹理 —— 海洋深度、沙漠/雨林/针叶林/苔原分带、极地冰盖、
  山脉起伏（高度梯度参与光照）、云层、夜间城市辉光；每帧只做双线性采样 + 地形光照，60 FPS
- **自转**：球面像素渲染、晨昏线、大气边缘光、经纬网、地轴、太阳直射点；只有自转视角，
  **没有节气定位**（节气属于公转）
- **公转**：太阳—轨道—地球全景、地轴方向空间固定、二分二至标记与一键定位、HUD
- **交互**：滚轮 / 工具栏 −＋ 缩放（0.6×–6×，带缓动）；暂停后拖动可旋转地球或沿轨道拖动公转位置；
  松手有惯性；缩放、视角、面板都有过渡动画
- 两个模型共用 `model/_engine` 的代码，各自 `src/` 只提供入口工厂；界面、文案、封面都在模型包里

## 模型源（git 仓库）

模型源就是普通 git 仓库，**仓库根目录下的 `model/` 文件夹即模型列表**。
支持 `https://github.com/<user>/<repo>.git`（GitLab / Gitee 同理，走 zip 归档，失败回退 `git clone --depth 1`）、
zip 直链、本地目录。导入后缓存在 `数据目录/cache/sources/<id>`，下载时把模型目录复制进本地 `model/`。

## 启动时运行内存分配

设置页的「启动时运行内存分配」类似启动器的 -Xmx：启动器把这份预算通过 `IModelContext.MemoryBudgetMb`
交给模型，具体怎么用（掩膜分辨率、缓存）由模型自己决定。
