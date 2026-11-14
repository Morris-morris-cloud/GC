using System;
using System.Threading;
using System.Threading.Tasks;
using GC.Models;
using GC.Services;

namespace GC.Download;

public sealed class DownloadModuleFactory : IDownloadModuleFactory
{
    public IDownloadModule Create(AppPaths paths, SettingsService settings, ModelRepository models) =>
        new DownloadModule(paths, settings, models);

    public bool TryApplyPending(AppPaths paths) => LauncherUpdater.TryApplyPending(paths);
}

internal sealed class DownloadModule : IDownloadModule
{
    public IDownloadCenter Downloads { get; }
    public IModelSourceClient Sources { get; }
    public IUpdateService Updates { get; }
    public ISourceSelector Selector { get; }
    public ILauncherUpdateService LauncherUpdates { get; }

    public DownloadModule(AppPaths paths, SettingsService settings, ModelRepository models)
    {
        Sources = new ModelSourceClient(paths);
        Downloads = new DownloadCenter(paths, models);
        Selector = new SourceSelector();
        Updates = new UpdateService(paths, settings, UpdateService.CreateRemote(settings.Current));
        LauncherUpdates = new LauncherUpdateService();
    }

    public IModelRemote CreateRemote(SourcePreset preset) => SourceSelector.CreateRemote(preset);
}

internal sealed class LauncherUpdateService : ILauncherUpdateService
{
    public Task<string?> CheckAsync(IModelRemote remote, CancellationToken ct = default) =>
        LauncherUpdater.CheckAsync(remote, ct);

    public Task<UpdateRunResult> DownloadAsync(AppPaths paths, IModelRemote remote,
        IProgress<UpdateProgressInfo>? progress = null, CancellationToken ct = default) =>
        LauncherUpdater.DownloadAsync(paths, remote, progress, ct);

    public bool ScheduleApply(AppPaths paths, string packageName) =>
        LauncherUpdater.ScheduleApply(paths, packageName);

    public bool TryApplyPending(AppPaths paths) => LauncherUpdater.TryApplyPending(paths);
}
