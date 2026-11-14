using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>仓库根 version.json：由构建脚本 VersionStamp 自动生成，模型一改版本就变。</summary>
public sealed class RepoVersion
{
    public int Schema { get; set; } = 1;
    public string Generated { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string ModelDir { get; set; } = "model";
    public RepoLauncher Launcher { get; set; } = new();
    public List<RepoModel> Models { get; set; } = new();

    // ---- 验签结论（解析时填入，不参与序列化） ----------------------------

    /// <summary>Verified / Skipped / Unsigned / Invalid。</summary>
    [JsonIgnore] public string SignatureTrust { get; set; } = "";

    [JsonIgnore] public string? SignatureKeyId { get; set; }

    [JsonIgnore] public string? SignatureError { get; set; }

    /// <summary>清单是否可用：签名有效，或本机没有配置公钥（兼容期）。</summary>
    [JsonIgnore]
    public bool IsTrusted =>
        SignatureTrust is nameof(ManifestTrust.Verified) or nameof(ManifestTrust.Skipped);
}

public sealed class RepoLauncher
{
    public string Version { get; set; } = "1.0.0";
    /// <summary>启动器源码指纹（自动算，仅展示，不触发升级）。</summary>
    public string Revision { get; set; } = "";
    public Dictionary<string, RepoPackage> Packages { get; set; } = new();
}

/// <summary>某个平台的启动器发布包；url 优先，否则用 path 去仓库里解析下载地址。</summary>
public sealed class RepoPackage
{
    public string? Url { get; set; }
    public string? Path { get; set; }
    public string? Sha256 { get; set; }
    public long Size { get; set; }
    /// <summary>Release 附件名（用于判断是 zip 还是单文件 exe / .app.zip）。</summary>
    public string? AssetName { get; set; }

    /// <summary>
    /// 仓库里的平台产物目录，如 <c>dist/mac-arm64</c>。
    /// 不发 Release、只把编译产物提交进仓库时，更新包就在这里找。
    /// 清单没写时由 <see cref="Rid"/> 推导。
    /// </summary>
    public string? Dir { get; set; }

    /// <summary>包文件名，如 <c>GC-1.0.7-mac-arm64.zip</c>；清单没写时按版本与平台标签拼。</summary>
    public string? Name { get; set; }

    /// <summary>所属平台键（win-x64 / win-arm64 / osx-arm64 …），解析清单时按字典键填入。</summary>
    [JsonIgnore] public string Rid { get; set; } = "";
}

public sealed class RepoModel
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>模型在仓库里的目录，如 model/earth-rotation。</summary>
    public string Dir { get; set; } = "";

    // ---- 完整性（由 VersionStamp 计算，签名覆盖这几个字段） ----------------

    /// <summary>内容指纹（构建溯源，不参与版本比较）。</summary>
    public string? Build { get; set; }

    /// <summary>模型清单 &lt;id&gt;.json 的 sha256。</summary>
    public string? ManifestSha256 { get; set; }

    /// <summary>&lt;id&gt;.model 目录的目录指纹（算法见 ContentHash）。</summary>
    public string? FilesSha256 { get; set; }

    /// <summary>参与分发的文件数。</summary>
    public int Files { get; set; }
}

/// <summary>远端目录树里的一个条目。</summary>
public sealed class RepoEntry
{
    public string Name { get; init; } = "";
    /// <summary>仓库内完整路径。</summary>
    public string Path { get; init; } = "";
    /// <summary>相对所请求根目录的路径，统一用 / 分隔。</summary>
    public string RelativePath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public string? DownloadUrl { get; init; }
    public long Size { get; init; }
    public string? Sha { get; init; }
}

/// <summary>
/// 远端源抽象：gitcode 是默认实现，本地目录实现用于离线教学与自动化测试。
/// </summary>
public interface IModelRemote
{
    string DisplayName { get; }

    /// <summary>源标识（xhmcl / gitcode / local），用于判断当前是哪个源。</summary>
    string SourceId => SourcePreset.Detect(DisplayName).Id;
    Task<RepoVersion?> GetVersionAsync(CancellationToken ct);
    Task<IReadOnlyList<RepoEntry>> ListTreeAsync(string rootPath, CancellationToken ct);
    Task<byte[]> DownloadAsync(RepoEntry entry, CancellationToken ct);
    Task<byte[]?> DownloadPackageAsync(RepoPackage package, CancellationToken ct);

    /// <summary>
    /// 最近一次读到的清单**原始文本**（含 signature 字段），供 SAVEDOG 二次验签。
    /// 默认返回 null，表示该源不支持（调用方会退化为单次验签）。
    /// </summary>
    Task<string?> GetRawVersionTextAsync(CancellationToken ct) => Task.FromResult<string?>(null);
}

public sealed class UpdateProgress
{
    public double Value { get; init; }
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
}

public enum UpdateOutcome
{
    NoChange,
    Applied,
    Partial,
    Unavailable,
    Disabled
}

public sealed class UpdateResult
{
    public UpdateOutcome Outcome { get; init; }
    public string Message { get; init; } = "";
    public int UpdatedModels { get; init; }
    public List<string> ModelNames { get; init; } = new();
    public string? LauncherMessage { get; init; }
    public string? Error { get; init; }
}
