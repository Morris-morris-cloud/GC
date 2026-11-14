# 模型库（model/）

这个目录是**模型**，包含模型自己的**源码 + 数据 + 功能程序集 + 封面 + 语言包**。
`src/`（启动器）只做四件事：扫描模型、下载模型、开窗口加载模型、检查更新。**启动器没有任何渲染逻辑，发布包也不内置模型**。

> 模型相关的一切都在 `model/` 里：共享引擎源码 `model/_engine/`、契约源码 `model/_sdk/`、
> 每个模型自己的源码 `model/<id>/src/`，编译产物与数据 `model/<id>/<id>.model/`。
> 仓库根还包含 `GC-DOWNLOAD/` 下载更新项目；这里的 `model/` 仍独立保存模型源码与可分发内容。

- 发布态模型库在 **`<可执行文件>/.GC/model/`**；开发态直接用仓库根 `model/`
- 优先级：`EMS_MODELS` 环境变量 → 开发态仓库根 `model/` → 发布态 `.GC/model/`
- 一个模型 = 一个文件夹，文件夹名即模型 id；`_sdk/`、`_engine/` 是下划线开头的共享目录，不会被当成模型
- 模型可以单独打包、单独分发，也可以放进 git 仓库当模型源

## 目录结构

```
model/
  version.json                     # 版本清单（与仓库根 version.json 相同，自动生成）
  _sdk/GC.ModelSdk/  # 模型契约（启动器与模型唯一的共享代码）
  _engine/                        # 共享引擎源码
    src/                          #   CelestialView / PlanetTexture / EarthModelView / SphereRaster…
    PreviewGen/                   #   用模型渲染器生成封面的小工具
    VersionStamp/                 #   生成 version.json（手写版本 + 完整性哈希）
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

## 版本、指纹与完整性

`./build.sh models`（或 `./build.sh stamp`）会调用 `model/_engine/VersionStamp`：

1. 读每个模型的 `<id>.json`，**保留作者手写的 `version`**（如 `1.6`）——
   版本号是对用户可见的语义版本，不由指纹自动覆盖；
2. 计算 `build`：`<id>.json`（去掉 `build` / `updatedAt`）与 `<id>.model/` 下所有文件
   按路径排序后的 SHA-256 指纹前 10 位，**仅用于构建溯源**；
3. 计算完整性哈希（客户端下载后会复算，不一致就丢弃）：
   - `manifestSha256` = 清单文件本身的 sha256
   - `filesSha256` = `<id>.model` 目录指纹
4. 生成仓库根 `version.json` 与 `model/version.json`（内容相同），再用
   `.signing/gc-signing.pem` 签名（ECDSA P-256），两份文件写的是**同一份签名**。

**改了模型内容必须手动升 `version`**，否则客户端认为版本没变，不会下载。
（`build` 会变，但它不参与版本比较。）

哈希算法（构建端与客户端共用 `GC/src/Security/ContentHash.cs`，不会出现两套实现）：

```
fileHash = SHA256(文件字节)
dirHash  = SHA256( 按相对路径 ordinal 升序，对每个文件拼接 "<相对路径>\0<fileHash>\n" )
```

相对路径统一 `/`，跳过 `.` 开头文件、`bin/`、`obj/`、`*.user`；不含时间戳/权限位。

安装成功后客户端会在模型目录写 `<id>.integrity.json`，每次加载 dll 前抽查
dll / json 的哈希 —— 本地文件被替换会直接拒绝加载。

## `<id>.json`（模型清单，启动器只读它）

```json
{
  "id": "earth-rotation",
  "name": "Earth Rotation",
  "nameI18n": { "zh-CN": "地球自转", "en": "Earth Rotation" },
  "description": "……",
  "descriptionI18n": { "zh-CN": "……", "en": "……" },
  "version": "1.6",
  "updatedAt": "2026-11-14T08:00:00Z",
  "author": "NeoRF",
  "tags": ["astronomy", "geography"],
  "assembly": "EarthRotationModel.dll",
  "type": "NeoRF.EarthModels.RotationModelFactory",
  "preview": "preview.png",
  "apiVersion": 1,
  "minAppVersion": "1.0.7"
}
```

| 字段 | 作用 |
| --- | --- |
| `version` | **作者手写**的语义版本；客户端靠它判断是否需要更新 |
| `assembly` | 功能程序集文件名，相对 `<id>.model`；留空按 `<id>.dll` 找 |
| `type` | 实现 `IModelFactory` 的完整类型名；留空则自动找第一个实现 |
| `preview` | 封面图，默认 `preview.png` |
| `apiVersion` | 模型契约版本，目前为 1；高于启动器支持版本会拒绝加载并提示升级 |
| `minAppVersion` | 可选，要求的最低启动器版本（如 `1.0.7`） |

`build` / `manifestSha256` / `filesSha256`（只出现在 `version.json` 里）由构建自动写入，
**不要手改**。

## 最小可分发结构

一个能跑的最小模型只需要三个文件：

```
model/hello/
  hello.json                  # 清单（id / version / assembly / type / apiVersion）
  hello.model/
    HelloModel.dll            # 实现 IModelFactory，返回任意 Avalonia 控件
```

其余（`preview.png`、`i18n/`、`scene.json`、`land.json`）都是可选的：
没有封面用默认灰图，没有语言包用清单里的 `name` / `description`。
源码放在同目录的 `src/` 里，`./build.sh models` 会自动编译并把 dll 复制进 `hello.model/`。


## 模型契约（model/_sdk）

模型只依赖一个极小的契约程序集 `GC.ModelSdk`（源码在 `model/_sdk/`）：

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
  在 `.GC/config/settings.json` 填 `updateLocalRoot` 指向它
- **手动导入**：在「下载」页粘贴任意 git 仓库 / zip / 本地目录，读取其中的 `model/` 文件夹

> 三种方式都会**验签**：`version.json` 必须由受信任私钥签名，否则客户端拒绝安装。
> 自己维护分支时，用 `dotnet run --project GC-SAVEDOG -- keygen` 生成密钥，
> 把公钥加进 `GC/src/Security/SignatureKeyring.cs`，再用 `--sign`/`build.sh stamp` 签名。
