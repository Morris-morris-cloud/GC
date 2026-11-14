using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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

/// <summary>一条下载任务（进入后台列表后一直保留，直到用户清除）。</summary>
public sealed partial class DownloadJob : ObservableObject
{
    public RemoteModel Model { get; }
    public string Id => Model.Id;
    public string Name => Model.LocalizedName;
    public string Version => Model.Manifest.Version;
    public CancellationTokenSource Cts { get; } = new();

    [ObservableProperty] private DownloadState _state = DownloadState.Queued;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _detailText = "";
    [ObservableProperty] private string? _error;

    public DownloadJob(RemoteModel model)
    {
        Model = model;
        RefreshText();
    }

    public bool IsActive => State is DownloadState.Queued or DownloadState.Running;
    public bool CanRetry => State is DownloadState.Failed or DownloadState.Canceled;
    public bool IsDone => State == DownloadState.Completed;

    public void RefreshText()
    {
        StatusText = State switch
        {
            DownloadState.Queued => L.I["dl.status.queued"],
            DownloadState.Running => L.I["dl.status.downloading"],
            DownloadState.Completed => L.I["dl.status.done"],
            DownloadState.Failed => L.I["dl.status.failed"],
            _ => L.I["dl.status.canceled"]
        };
        DetailText = State == DownloadState.Failed && !string.IsNullOrEmpty(Error)
            ? Error!
            : ModelScanner.HumanSize(Model.SizeBytes);
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsDone));
    }

    partial void OnStateChanged(DownloadState value) => RefreshText();
    partial void OnErrorChanged(string? value) => RefreshText();
}

/// <summary>下载中心：最多 2 个并发，任务在「后台列表」里折叠展示。</summary>
public sealed class DownloadCenter
{
    private readonly ModelRepository _repository;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _slots = new(2);

    public ObservableCollection<DownloadJob> Jobs { get; } = new();
    public event Action? Installed;

    public DownloadCenter(AppPaths paths, ModelRepository repository)
    {
        _paths = paths;
        _repository = repository;
    }

    public DownloadJob Enqueue(RemoteModel model)
    {
        var running = Jobs.FirstOrDefault(j => j.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase) && j.IsActive);
        if (running != null) return running;

        var job = new DownloadJob(model);
        Jobs.Insert(0, job);
        _ = RunAsync(job);
        return job;
    }

    private async Task RunAsync(DownloadJob job)
    {
        // 在 UI 线程构造，回调自动回到 UI 线程
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            job.Progress = p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0;
        });

        try
        {
            await _slots.WaitAsync(job.Cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            job.State = DownloadState.Canceled;
            return;
        }

        try
        {
            job.State = DownloadState.Running;
            await ModelInstaller.InstallAsync(job.Model, _paths.ModelRoot, progress, job.Cts.Token)
                .ConfigureAwait(true);
            job.Progress = 1;
            job.State = DownloadState.Completed;
            _repository.NotifyChanged();
            Installed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            job.State = DownloadState.Canceled;
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            job.State = DownloadState.Failed;
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Cancel(DownloadJob job)
    {
        try { job.Cts.Cancel(); } catch { }
        if (job.State == DownloadState.Queued) job.State = DownloadState.Canceled;
    }

    public void Retry(DownloadJob job)
    {
        if (job.IsActive) return;
        Jobs.Remove(job);
        Enqueue(job.Model);
    }

    public void Remove(DownloadJob job)
    {
        Cancel(job);
        Jobs.Remove(job);
    }

    public void ClearFinished()
    {
        for (var i = Jobs.Count - 1; i >= 0; i--)
        {
            if (Jobs[i].State is DownloadState.Completed or DownloadState.Canceled)
                Jobs.RemoveAt(i);
        }
    }

    public int ActiveCount => Jobs.Count(j => j.IsActive);
}
