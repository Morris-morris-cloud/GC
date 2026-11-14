using System;
using System.Collections.Generic;
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
}

public sealed class RepoModel
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>模型在仓库里的目录，如 model/earth-rotation。</summary>
    public string Dir { get; set; } = "";
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
