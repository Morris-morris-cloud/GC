using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using GC.Models;

namespace GC.Services;

public enum DownloadState
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled
}

public interface IDownloadJob : INotifyPropertyChanged
{
    RemoteModel Model { get; }
    string Id { get; }
    string Name { get; }
    string Version { get; }
    CancellationTokenSource Cts { get; }
    DownloadState State { get; set; }
    double Progress { get; set; }
    string StatusText { get; }
    string DetailText { get; }
    string? Error { get; set; }
    bool IsActive { get; }
    bool CanRetry { get; }
    bool IsDone { get; }
    void RefreshText();
}

public interface IDownloadCenter
{
    ObservableCollection<IDownloadJob> Jobs { get; }
    event Action? Installed;
    IDownloadJob Enqueue(RemoteModel model);
    void Cancel(IDownloadJob job);
    void Retry(IDownloadJob job);
    void Remove(IDownloadJob job);
    void ClearFinished();
    int ActiveCount { get; }
}

public interface IModelSourceClient
{
    Task<SourceIndex> FetchAsync(ModelSource source, bool force, IProgress<string>? log, CancellationToken ct);
}

public interface IUpdateService
{
    IModelRemote Remote { get; }
    void UseRemote(IModelRemote remote);
    Task<UpdateResult> RunAsync(bool force, IProgress<UpdateProgress>? progress, CancellationToken ct = default);
}

public interface ISourceSelector
{
    SourcePreset? Current { get; }
    System.Collections.Generic.IReadOnlyDictionary<string, long> LastLatency { get; }
    Task<SourcePreset> PickFastestAsync(bool force = false, CancellationToken ct = default);
}

public interface ILauncherUpdateService
{
    Task<string?> CheckAsync(IModelRemote remote, CancellationToken ct = default);
    Task<UpdateRunResult> DownloadAsync(AppPaths paths, IModelRemote remote,
        IProgress<UpdateProgressInfo>? progress = null, CancellationToken ct = default);
    bool ScheduleApply(AppPaths paths, string packageName);
    bool TryApplyPending(AppPaths paths);
}

public enum UpdateRunState { UpToDate, Ready, Failed }

public sealed class UpdateProgressInfo
{
    public double Value { get; init; }
    public string Stage { get; init; } = "";
}

public sealed class UpdateRunResult
{
    public UpdateRunState State { get; init; }
    public string Version { get; init; } = "";
    public string Message { get; init; } = "";
    public string? PackageName { get; init; }
    public string? NewAppPath { get; init; }
}

public interface IDownloadModule
{
    IDownloadCenter Downloads { get; }
    IModelSourceClient Sources { get; }
    IUpdateService Updates { get; }
    ISourceSelector Selector { get; }
    ILauncherUpdateService LauncherUpdates { get; }
    IModelRemote CreateRemote(SourcePreset preset);
}

public interface IDownloadModuleFactory
{
    IDownloadModule Create(AppPaths paths, SettingsService settings, ModelRepository models);
    bool TryApplyPending(AppPaths paths);
}

public static class DownloadModuleHost
{
    public static IDownloadModuleFactory? Factory { get; set; }
}

public static class LauncherRuntimeInfo
{
    private static string? _versionOverride;
    public static string CurrentLauncherVersion
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_versionOverride)) return _versionOverride!;
            var assembly = typeof(AppServices).Assembly;
            var version = assembly.GetName().Version;
            if (version == null || (version.Major == 0 && version.Minor == 0 && version.Build == 0))
            {
                var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<
                    System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info) && Version.TryParse(info.Split('+')[0].Trim(), out var parsed))
                    version = parsed;
            }
            return version == null
                ? "0.0.0"
                : $"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
        }
    }

    public static string CurrentRid
    {
        get
        {
            var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                _ => "x64"
            };
            if (OperatingSystem.IsWindows()) return "win-" + architecture;
            if (OperatingSystem.IsMacOS()) return "osx-" + architecture;
            if (OperatingSystem.IsLinux()) return "linux-" + architecture;
            return System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        }
    }

    public static void OverrideLauncherVersion(string? version) => _versionOverride = version;
}

/// <summary>
/// 界面（Flutter）与后端（GC.UI.Server）之间的约定：端口、环境变量名。
///
/// 之前这个端口在 GC-BOOT、后端、Flutter 前端各写了一遍 17890，改一处就断链；
/// 现在统一由这里定义，启动器选好端口后通过环境变量传给另外两边。
/// </summary>
public static class GcUiContract
{
    /// <summary>默认端口（与历史版本保持一致，便于复用已在运行的后端）。</summary>
    public const int DefaultPort = 17890;

    /// <summary>端口被占用时最多向后尝试几个。</summary>
    public const int PortScanRange = 10;

    /// <summary>传给后端与前端的环境变量名。</summary>
    public const string PortEnvVar = "GC_UI_PORT";

    /// <summary>
    /// 启动器为本次会话生成的一次性令牌，经环境变量同时传给进程内后端与 Flutter 前端。
    /// 前端每个请求都要带该令牌，后端据此确认对方是自己拉起的那个前端；
    /// 同机伪装进程即便占了端口，也没有令牌，无法被前端当成后端复用。
    /// </summary>
    public const string TokenEnvVar = "GC_UI_TOKEN";

    /// <summary>前端携带一次性令牌的请求头。</summary>
    public const string TokenHeader = "X-GC-UI-Token";

    /// <summary>读取环境变量里指定的端口；非法或未设置时返回 <see cref="DefaultPort"/>。</summary>
    public static int PortFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(PortEnvVar);
        return int.TryParse(value, out var port) && port > 0 && port < 65536
            ? port
            : DefaultPort;
    }

    /// <summary>生成 256 位会话令牌（hex）；cryptographically random，无法预测。</summary>
    public static string NewSessionToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 由令牌派生一段短指纹，后端在 /api/health 里回给启动器用于身份确认。
    /// 回指纹而非令牌本身，避免会话令牌出现在明文 HTTP 响应体里；启动器用同一函数
    /// 算期望值比对，不匹配的「健康」响应一律视为伪装服务。
    /// </summary>
    public static string DeriveSessionProof(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "";
        var bytes = System.Text.Encoding.UTF8.GetBytes("gc-health-proof:" + token);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
