# 模型库（model/）

这个目录是**模型**，包含模型自己的**源码 + 数据 + 功能程序集 + 封面 + 语言包**。
`src/`（启动器）只做四件事：扫描模型、下载模型、开窗口加载模型、检查更新。**启动器没有任何渲染逻辑，发布包也不内置模型**。

> 模型相关的一切都在 `model/` 里：共享引擎源码 `model/_engine/`、契约源码 `model/_sdk/`、
> 每个模型自己的源码 `model/<id>/src/`，编译产物与数据 `model/<id>/<id>.model/`。
> 仓库根目录只有 `src/`（启动器）和 `model/`，没有任何旁路目录。

- 发布态模型库在 **`<可执行文件>/.GC/model/`**；开发态直接用仓库根 `model/`
- 优先级：`EMS_MODELS` 环境变量 → 开发态仓库根 `model/` → 发布态 `.GC/model/`
- 一个模型 = 一个文件夹，文件夹名即模型 id；`_sdk/`、`_engine/` 是下划线开头的共享目录，不会被当成模型
- 模型可以单独打包、单独分发，也可以放进 git 仓库当模型源

## 目录结构

```
model/
  version.json                     # 版本清单（与仓库根 version.json 相同，自动生成）
  _sdk/EarthModelStudio.ModelSdk/  # 模型契约（启动器与模型唯一的共享代码）
  _engine/                        # 共享引擎源码
    src/                          #   CelestialView / PlanetTexture / EarthModelView / SphereRaster…
    PreviewGen/                   #   用模型渲染器生成封面的小工具
    VersionStamp/                 #   按内容指纹自动打版本、生成 version.json
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

## 版本是自动改的

`./build.sh models`（或 `./build.sh stamp`）会调用 `model/_engine/VersionStamp`：

1. 对每个模型，把 `<id>.json`（去掉 `version` / `updatedAt`）与 `<id>.model/` 下所有文件
   按路径排序做 SHA-256 指纹，得到版本号 `1.0-<10位指纹>`；
2. 把这个版本写回 `<id>.json` 的 `version` 与 `updatedAt`；
3. 生成仓库根 `version.json` 和 `model/version.json`（内容相同），里面记录
   启动器版本、各平台更新包地址、每个模型的 id / 版本 / 仓库目录。

**所以只要改了模型文件，版本号就自动变**，不需要手工维护；内容没变时重复构建版本号保持不变。
客户端启动时读 `version.json`，和 `.GC/settings.json` 里记录的版本一比对就知道要不要下载。

> 改了 `scene.json` 或引擎源码后，跑 `./build.sh models`，再把 `version.json` 和 `model/`
> 推送到 gitcode；客户端下次启动会下载新模型并**本次启动立即生效**。
> 启动器自身更新包由 `./build.sh publish` 生成（`dist/GC-<rid>.zip`），下载后**下次启动才应用**。

## `<id>.json`（模型清单，启动器只读它）

```json
{
  "id": "earth-rotation",
  "name": "Earth Rotation",
  "nameI18n": { "zh-CN": "地球自转", "en": "Earth Rotation" },
  "description": "……",
  "descriptionI18n": { "zh-CN": "……", "en": "……" },
  "version": "1.0-3f9a2c1b7d",
  "updatedAt": "2026-11-07T08:00:00Z",
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
| `version` | **自动生成**，模型包内容指纹；客户端靠它判断是否需要更新 |
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
  - `orbit.seasonLayout`：`springTopSummerLeft` = **上春分、左夏至**（下秋分、右冬至），季节角度与直射纬度读数不变
  - `planet.axialTiltDeg` / `planet.tropicDeg`：教材值 **23.5°**（极圈自动算 90 − 23.5 = 66.5°）
  - `orbit.seasons[].declinationDeg`：二分二至的太阳直射纬度（0 / +23.5 / 0 / −23.5），
    定格时读数直接取这个值，不用近似公式
  - `orbit.seasons[].teachingI18n`：该节气的课堂现象说明（中英双语，写在模型数据里）
  - `orbit.seasons[].date`：教材日期（3/21、6/22、9/23、12/22），定格时 HUD 与课堂卡都用它
- `land.json`：大陆轮廓，`{ "polygons": [ { "name": "...", "points": [[lon,lat], ...] } ] }`

## 语言包

`<id>.model/i18n/<language>.json` 是扁平的 `键: 文案` 映射，模型自己读取。
地球模型用到的键：`title`、`view.rotation`、`play`、`pause`、`speed`、`reset`、`labels`、`orbit`、`grid`、`tilt`、
`lessons`、`seasons`、`close`、`sun`、`earth`、`north`、`south`、`subsolar`、`spinInfo`、`orbitInfo`、
`declinationInfo`、`dateInfo`、`season.*`。

## 封面

封面是 `preview.png`，由 `model/_engine/PreviewGen` 用**模型自己的渲染器**生成：

```bash
./build.sh models        # 编译模型 dll + 生成所有模型封面 + 自动打版本
```

## 做一个新模型

1. 在 `model/<id>/src/` 里新建类库项目（可引用 `model/_engine/src` 的共享代码，也可完全自己写）
2. 写一个 `IModelFactory` 实现：`Prepare` 里读数据算纹理并 `ReportProgress`，`CreateView` 里返回控件
3. 在 `model/<id>/` 放清单和 `<id>.model/` 数据目录，用 `assembly` / `type` 指向你的 dll
4. `./build.sh models` 会自动编译每个模型、把 dll 复制进对应 `<id>.model/`、生成封面、打版本并写进 `version.json`
5. 推送仓库，客户端下次启动自动下载 —— 不用改启动器、不用重新发布程序

## 作为模型源分发

- **自动更新（推荐）**：把 `version.json` 和 `model/` 推到 gitcode 仓库，启动器开机自检并下载到 `.GC/model/`
- **离线源**：把整个仓库目录（含 `version.json` + `model/`）拷到 U 盘 / 共享盘，
  在 `.GC/settings.json` 填 `updateLocalRoot` 指向它
- **手动导入**：在「下载」页粘贴任意 git 仓库 / zip / 本地目录，读取其中的 `model/` 文件夹
