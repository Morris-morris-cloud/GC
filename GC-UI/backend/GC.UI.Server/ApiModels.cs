using System.Collections.Generic;

namespace GC.UI.Server;

// ---------------------------------------------------------------------------
// 前后端之间的数据契约（HTTP + JSON，camelCase）。
// 前端（Flutter）只认这些字段，不认 .NET 类型；后端换实现不影响前端。
// ---------------------------------------------------------------------------

public sealed record HealthDto(
    bool Ok,
    string Version,
    string Platform,
    bool UiAvailable,
    string? UiError,
    string? SessionProof = null);

public sealed record ModelDto(
    string Id,
    string Name,
    string Description,
    string Version,
    string Author,
    long SizeBytes,
    string SizeText,
    IReadOnlyList<string> Tags,
    bool HasPreview,
    string PreviewUrl,
    string Directory,
    string DataDirectory);

public sealed record SourceDto(
    string Id,
    string Name,
    string Url,
    bool Enabled,
    bool BuiltIn,
    string Host);

public sealed record RemoteModelDto(
    string Id,
    string Name,
    string Description,
    string Version,
    long SizeBytes,
    string SizeText,
    string SourceId,
    bool Installed,
    string? InstalledVersion);

public sealed record SourceIndexDto(
    string SourceId,
    string Root,
    bool FromCache,
    string? Error,
    string ResolvedFrom,
    IReadOnlyList<RemoteModelDto> Models);

public sealed record JobDto(
    string Id,
    string ModelId,
    string Name,
    string Version,
    string State,
    double Progress,
    string StatusText,
    string DetailText,
    string? Error,
    bool IsActive,
    bool CanRetry,
    bool IsDone);

public sealed record LanguageDto(string Code, string Label);

public sealed record SettingsDto(
    string Language,
    string Theme,
    string AccentColor,
    int RenderFps,
    int MemoryMb,
    bool MemoryAuto,
    bool AutoUpdateOnStartup,
    bool RequireSignedManifest,
    string UpdateRepository,
    string? UpdateLocalRoot,
    string? ModelRootOverride,
    int MemoryBudgetMb,
    string MemoryUsageText,
    string ModelRoot,
    string DataRoot,
    string SettingsFile,
    string Version,
    string Platform,
    IReadOnlyList<SourceDto> Sources,
    IReadOnlyList<LanguageDto> Languages);

/// <summary>设置补丁：只改传进来的字段，null 表示不动。</summary>
public sealed record SettingsPatch(
    string? Language,
    string? Theme,
    string? AccentColor,
    int? RenderFps,
    int? MemoryMb,
    bool? MemoryAuto,
    bool? AutoUpdateOnStartup,
    bool? RequireSignedManifest,
    string? UpdateRepository,
    string? UpdateLocalRoot,
    string? ModelRootOverride);

public sealed record LatencyDto(string Id, string Name, long Milliseconds);

public sealed record UpdateCheckDto(
    string Outcome,
    string Message,
    int UpdatedModels,
    IReadOnlyList<string> ModelNames,
    string? LauncherMessage,
    string? Error,
    string? LauncherLatest,
    string SourceName,
    IReadOnlyList<LatencyDto> Latency);

public sealed record ContributorDto(string Name, int Contributions, string? AvatarUrl);

// ---- 用户 API DTO ----
public sealed record RegisterRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password);
public sealed record ThemeRequest(string Color, int Cost);


/// <summary>「重新下载启动器」结果：State = Ready / UpToDate / Failed。</summary>
public sealed record LauncherRedownloadDto(
    string State,
    string Version,
    string Message,
    IReadOnlyList<string> Steps);

public sealed record AboutDto(
    string AppName,
    string Version,
    string Platform,
    string Repository,
    IReadOnlyList<ContributorDto> Contributors,
    string SourceLanguage,
    string Frontend);

public sealed record LaunchResultDto(bool Ok, string Message);

/// <summary>
/// 通用错误响应。限流 / 参数校验这类与具体业务无关的失败用它，
/// 不要复用 <see cref="LaunchResultDto"/> —— 那会让前端误以为「启动模型」有结果。
/// </summary>
public sealed record ErrorDto(bool Ok, string Message)
{
    public static ErrorDto Of(string message) => new(false, message);
}

public sealed record DownloadRequest(string SourceId, string ModelId);

public sealed record PathRequest(string Path);

public sealed record UrlRequest(string Url);

public sealed record ForceRequest(bool Force);
