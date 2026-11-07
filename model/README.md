# 模型库（model/）

这个目录是**模型**，包含模型自己的**源码 + 数据 + 功能程序集 + 封面 + 语言包**。
`src/`（启动器）只做三件事：扫描模型、下载模型、开窗口加载模型。**启动器没有任何渲染逻辑**。

> 模型相关的一切都在 `model/` 里：共享引擎源码 `model/_engine/`、契约源码 `model/_sdk/`、
> 每个模型自己的源码 `model/<id>/src/`，编译产物与数据 `model/<id>/<id>.model/`。
> 仓库根目录只有 `src/`（启动器）和 `model/`，没有任何旁路目录。

- 启动器查找模型库的顺序：`EMS_MODELS` 环境变量 → 开发态仓库根 `model/` → 可执行文件旁边 `model/` → 用户数据目录
- 一个模型 = 一个文件夹，文件夹名即模型 id；`_sdk/`、`_engine/` 是下划线开头的共享目录，不会被当成模型
- 模型可以单独打包、单独分发，也可以放进 git 仓库当模型源

## 目录结构

```
model/
  _sdk/EarthModelStudio.ModelSdk/ # 模型契约（启动器与模型唯一的共享代码）
  _engine/                        # 共享引擎源码（可选，只有地球模型用）
    src/                          #   CelestialView / PlanetTexture / EarthModelView / SphereRaster…
    PreviewGen/                   #   用模型渲染器生成封面的小工具
  <模型 id>/
    <模型 id>.json                # 清单：名称、i18n、版本、程序集入口、封面
    src/                          # 模型自己的源码（csproj + IModelFactory 实现）
    <模型 id>.model/              # 模型包（可分发的部分）
      EarthRotationModel.dll      #   功能程序集（实现 IModelFactory）
      scene.json                  #   场景数据（内容由模型定义）
      land.json                   #   贴图/轮廓数据（可选）
      preview.png                 #   封面图（启动器只显示，不渲染）
      i18n/zh-CN.json             #   界面文案（模型自己读）
      i18n/en.json
```

## `<id>.json`（模型清单，启动器只读它）

```json
{
  "id": "earth-rotation",
  "name": "Earth Rotation",
  "nameI18n": { "zh-CN": "地球自转", "en": "Earth Rotation" },
  "description": "……",
  "descriptionI18n": { "zh-CN": "……", "en": "……" },
  "version": "1.0.0",
  "author": "NeoRF",
  "tags": ["astronomy", "geography"],
  "assembly": "EarthRotationModel.dll",
  "type": "NeoRF.EarthModels.RotationModelFactory",
  "preview": "preview.png",
  "apiVersion": 1
}
```

| 字段 | 作用 |
| --- | --- |
| `assembly` | 功能程序集文件名，相对 `<id>.model`；留空按 `<id>.dll` 找 |
| `type` | 实现 `IModelFactory` 的完整类型名；留空则自动找第一个实现 |
| `preview` | 封面图，默认 `preview.png` |
| `apiVersion` | 模型契约版本，目前为 1 |

## 模型契约（model/_sdk）

模型只依赖一个极小的契约程序集 `EarthModelStudio.ModelSdk`（源码在 `model/_sdk/`）：

```csharp
public interface IModelFactory
{
    int ApiVersion { get; }
    IModelRuntime Create();
}

public interface IModelRuntime : IDisposable
{
    void Prepare(IModelContext context);         // 后台线程：读数据、算纹理，可汇报进度
    Control CreateView(IModelContext context);   // UI 线程：构建界面
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

模型 `CreateView` 返回的控件会铺满窗口客户区 —— **工具栏、动画、文字、交互全由模型自己实现**。
启动器只提供窗口、语言、内存预算和关闭能力。

## `scene.json` / `land.json`

这两个文件的内容完全由模型自己定义（启动器不解析）。地球模型用的是：

- `scene.json`：`defaultView`、`views`、行星参数、颜色、轨道、`lessons` 教学要点
- `land.json`：大陆轮廓，`{ "polygons": [ { "name": "...", "points": [[lon,lat], ...] } ] }`

## 语言包

`<id>.model/i18n/<language>.json` 是扁平的 `键: 文案` 映射，模型自己读取。
地球模型用到的键：`title`、`view.rotation`、`play`、`pause`、`speed`、`reset`、`labels`、`orbit`、`grid`、`tilt`、
`lessons`、`seasons`、`close`、`sun`、`earth`、`north`、`south`、`subsolar`、`spinInfo`、`orbitInfo`、
`declinationInfo`、`dateInfo`、`season.*`。

## 封面

封面是 `preview.png`，由 `model/_engine/PreviewGen` 用**模型自己的渲染器**生成：

```bash
./build.sh models        # 编译模型 dll + 生成所有模型封面
```

## 做一个新模型

1. 在 `model/<id>/src/` 里新建类库项目（可引用 `model/_engine/src` 的共享代码，也可完全自己写）
2. 写一个 `IModelFactory` 实现：`Prepare` 里读数据算纹理并 `ReportProgress`，`CreateView` 里返回控件
3. 在 `model/<id>/` 放清单和 `<id>.model/` 数据目录，用 `assembly` / `type` 指向你的 dll
4. `./build.sh models` 会自动编译每个模型、把 dll 复制进对应 `<id>.model/`，并生成封面

## 作为模型源分发

把 `model/` 放到任意 git 仓库根目录并推送，用户在「下载」页粘贴仓库链接即可；
启动器读取仓库里的 `model/` 文件夹，一栏一个模型。
