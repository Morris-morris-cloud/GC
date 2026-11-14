using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GC.Models;
using GC.Services;

namespace GC.ViewModels;

public partial class DownloadViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private bool _initialized;
    // 每次切源取消上一次加载，并用 generation 序号丢弃过期结果，避免并发写 Models 集合
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = new();
    public ObservableCollection<RemoteModelRowViewModel> Models { get; } = new();
    public ObservableCollection<DownloadJob> Jobs => _services.Downloads.Jobs;

    [ObservableProperty] private SourceItemViewModel? _selectedSource;
    [ObservableProperty] private string _importUrl = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasModels;
    [ObservableProperty] private bool _isBackgroundOpen = true;
    [ObservableProperty] private string _jobSummary = "";
    [ObservableProperty] private bool _hasJobs;
    [ObservableProperty] private bool _isSourceEmpty;

    public DownloadViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        _main = main;
        Jobs.CollectionChanged += OnJobsChanged;
        foreach (var job in Jobs) job.PropertyChanged += OnJobPropertyChanged;
        L.I.LanguageChanged += RefreshLanguage;
        RebuildSources();
        UpdateJobState();
    }

    public string BackgroundTitle => L.I["dl.background"];
    public string ImportHint => L.I["dl.hint"];

    // ------------------------------------------------------------ 页面生命周期
    public void OnShown()
    {
        if (_initialized) return;
        _initialized = true;
        if (SelectedSource == null)
            SelectedSource = Sources.FirstOrDefault(s => s.Model.Enabled && !string.IsNullOrWhiteSpace(s.Model.Url))
                             ?? Sources.FirstOrDefault();
    }

    // ------------------------------------------------------------ 模型源
    private void RebuildSources()
    {
        var keepId = SelectedSource?.Model.Id;
        Sources.Clear();
        foreach (var source in _services.Settings.Current.Sources)
            Sources.Add(new SourceItemViewModel(source, this, _services));

        SelectedSource = Sources.FirstOrDefault(s => s.Model.Id == keepId) ?? SelectedSource;
    }

    partial void OnSelectedSourceChanged(SourceItemViewModel? value)
    {
        if (value == null) return;
        if (string.IsNullOrWhiteSpace(value.Model.Url))
        {
            Models.Clear();
            HasModels = false;
            IsSourceEmpty = true;
            StatusMessage = L.I["dl.pickSource"];
            return;
        }
        _ = LoadAsync(value, force: false);
    }

    internal void OnSourceEnabledChanged(SourceItemViewModel source)
    {
        if (!source.Model.Enabled && SelectedSource == source) Models.Clear();
    }

    internal void RemoveSource(SourceItemViewModel source)
    {
        if (source.Model.BuiltIn) return;   // 内置默认源不允许删除
        _services.Settings.Current.Sources.Remove(source.Model);
        _services.Settings.Save();
        var wasSelected = SelectedSource == source;
        Sources.Remove(source);
        if (wasSelected)
        {
            SelectedSource = Sources.FirstOrDefault();
            if (SelectedSource == null)
            {
                Models.Clear();
                HasModels = false;
            }
        }
    }

    /// <summary>
    /// 只允许两个内置源（Xhmcl / GitCode），不再接受任意地址。
    /// 旧的「自定义源」入口统一改为：按测速结果切换到更快的那个源。
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var fastest = await _services.Selector.PickFastestAsync(force: true);
        var target = Sources.FirstOrDefault(x =>
            SourcePreset.Find(fastest.Id) != null &&
            SourcePreset.Detect(x.Model.Url).Id == fastest.Id);

        if (target == null)
        {
            StatusMessage = "没有可用的下载源";
            return;
        }

        SelectedSource = target;
        var lat = _services.Selector.LastLatency;
        StatusMessage = $"已切换到 {fastest.Name}" +
            (lat.Count > 0 ? "（" + string.Join("，", lat.Select(kv =>
                $"{SourcePreset.Find(kv.Key)?.Name ?? kv.Key} {kv.Value}ms")) + "）" : "");
        await LoadAsync(target, force: true);
    }

    private static string DeriveName(string url)
    {
        var trimmed = url.TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return string.IsNullOrWhiteSpace(name) ? url : name;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedSource == null) return;
        await LoadAsync(SelectedSource, force: true);
    }

    internal async Task RefreshFromSource(SourceItemViewModel source)
    {
        SelectedSource = source;
        await LoadAsync(source, force: true);
    }

    private async Task LoadAsync(SourceItemViewModel source, bool force)
    {
        if (!source.Model.Enabled) return;

        // 取消上一次尚未完成的加载，避免快速切换源时并发 Clear/写入同一集合
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        var generation = ++_loadGeneration;

        IsBusy = true;
        StatusMessage = L.I["dl.syncing"];
        try
        {
            // 第 6 项：下载页与启动更新用同一个源、同一种下载方式。
            // 每次加载前先让选择器给出最快源（有缓存，不会真的每次都测）。
            try
            {
                var fastest = await _services.Selector.PickFastestAsync(false, ct);
                if (fastest.Id != SourcePreset.Detect(source.Model.Url).Id)
                {
                    var prefer = Sources.FirstOrDefault(x =>
                        SourcePreset.Detect(x.Model.Url).Id == fastest.Id && x.Model.Enabled);
                    if (prefer != null && prefer != source)
                    {
                        SelectedSource = prefer;
                        source = prefer;
                    }
                }
            }
            catch { /* 测速失败就沿用当前源 */ }

            var index = await _services.Sources.FetchAsync(source.Model, force, null, ct);
            // 只有「仍是当前选中源 + 仍是最新一次加载」才可以写 UI 集合
            if (ct.IsCancellationRequested || generation != _loadGeneration || SelectedSource != source)
                return;

            if (index.Error != null)
            {
                Models.Clear();
                HasModels = false;
                IsSourceEmpty = true;
                StatusMessage = DescribeError(index.Error);
                return;
            }

            var installedIds = new HashSet<string>(
                _services.Models.Scan().Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

            Models.Clear();
            foreach (var model in index.Models)
            {
                Models.Add(new RemoteModelRowViewModel(model, source,
                    installedIds.Contains(model.Id), DownloadCommand));
            }

            HasModels = Models.Count > 0;
            IsSourceEmpty = Models.Count == 0;
            StatusMessage = index.FromCache
                ? $"{L.I.F("dl.ready", Models.Count)} · {L.I["dl.fromCache"]}"
                : L.I.F("dl.ready", Models.Count);
        }
        catch (OperationCanceledException)
        {
            // 被更新的加载取代或页面关闭：忽略，不覆盖新结果
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) StatusMessage = ex.Message;
        }
        finally
        {
            // 只有最后一次加载才复位 Busy，避免旧任务把新任务的忙碌状态清掉
            if (generation == _loadGeneration) IsBusy = false;
        }
    }

    private static string DescribeError(string error)
    {
        if (error.StartsWith("dl.", StringComparison.Ordinal))
        {
            var text = L.I[error];
            return text == error ? error : text;
        }
        return error;
    }

    // ------------------------------------------------------------ 下载
    [RelayCommand]
    private async Task DownloadAsync(RemoteModelRowViewModel row)
    {
        if (row == null) return;
        row.RowOpacity = 0;
        row.RowHeight = 0;
        IsBackgroundOpen = true;

        _services.Downloads.Enqueue(row.Model);

        await Task.Delay(240);
        Models.Remove(row);
        HasModels = Models.Count > 0;
        IsSourceEmpty = Models.Count == 0;
    }

    [RelayCommand]
    private void CancelJob(DownloadJob? job)
    {
        if (job != null) _services.Downloads.Cancel(job);
    }

    [RelayCommand]
    private void RetryJob(DownloadJob? job)
    {
        if (job != null) _services.Downloads.Retry(job);
    }

    [RelayCommand]
    private void RemoveJob(DownloadJob? job)
    {
        if (job != null) _services.Downloads.Remove(job);
    }

    [RelayCommand]
    private void ClearFinished() => _services.Downloads.ClearFinished();

    [RelayCommand]
    private void ToggleBackground() => IsBackgroundOpen = !IsBackgroundOpen;

    // ------------------------------------------------------------ 任务展示
    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (DownloadJob job in e.NewItems) job.PropertyChanged += OnJobPropertyChanged;
        if (e.OldItems != null)
            foreach (DownloadJob job in e.OldItems) job.PropertyChanged -= OnJobPropertyChanged;
        UpdateJobState();
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadJob.State))
        {
            if (sender is DownloadJob { State: DownloadState.Completed })
                _services.Models.NotifyChanged();
        }
        UpdateJobState();
    }

    private void UpdateJobState()
    {
        HasJobs = Jobs.Count > 0;
        JobSummary = $"{L.I["dl.background"]} ({Jobs.Count})";
        OnPropertyChanged(nameof(JobSummary));
    }

    private void RefreshLanguage()
    {
        foreach (var source in Sources) source.RefreshLanguage();
        foreach (var model in Models) model.RefreshLanguage();
        foreach (var job in Jobs) job.RefreshText();
        OnPropertyChanged(nameof(BackgroundTitle));
        OnPropertyChanged(nameof(ImportHint));
        UpdateJobState();
    }
}

public partial class SourceItemViewModel : ObservableObject
{
    private readonly DownloadViewModel _owner;
    private readonly AppServices _services;

    public ModelSource Model { get; }

    public SourceItemViewModel(ModelSource model, DownloadViewModel owner, AppServices services)
    {
        Model = model;
        _owner = owner;
        _services = services;
        RemoveCommand = new RelayCommand(() => _owner.RemoveSource(this));
        RefreshCommand = new AsyncRelayCommand(async () => await _owner.RefreshFromSource(this));
    }

    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? Model.Id : Model.Name;
    public string HostText => string.IsNullOrWhiteSpace(Model.Url) ? L.I["dl.noSource"] : Model.DisplayHost;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Model.Url);
    /// <summary>内置默认源不可删除（下载页隐藏删除按钮）。</summary>
    public bool CanRemove => !Model.BuiltIn;

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            _services.Settings.Save();
            OnPropertyChanged();
            _owner.OnSourceEnabledChanged(this);
        }
    }

    public ICommand RemoveCommand { get; }
    public ICommand RefreshCommand { get; }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HostText));
    }
}

public partial class RemoteModelRowViewModel : ObservableObject
{
    public RemoteModel Model { get; }
    public SourceItemViewModel Source { get; }

    public RemoteModelRowViewModel(RemoteModel model, SourceItemViewModel source, bool installed, ICommand downloadCommand)
    {
        Model = model;
        Source = source;
        Installed = installed;
        DownloadCommand = downloadCommand;
    }

    public string Name => Model.LocalizedName;
    public string Description => Model.LocalizedDescription;
    public string VersionText => "v" + Model.Manifest.Version;
    public string SizeText => ModelScanner.HumanSize(Model.SizeBytes);
    public string MetaText => $"{VersionText}  ·  {SizeText}";
    public string TagsText => string.Join("  ·  ", Model.Manifest.Tags ?? new List<string>());
    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);

    [ObservableProperty] private bool _installed;
    [ObservableProperty] private double _rowHeight = 92;
    [ObservableProperty] private double _rowOpacity = 1;

    public string ButtonText => Installed ? L.I["dl.reinstall"] : L.I["dl.download"];
    public ICommand DownloadCommand { get; }

    partial void OnInstalledChanged(bool value) => OnPropertyChanged(nameof(ButtonText));

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(ButtonText));
    }
}
