using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthModelStudio.Models;
using EarthModelStudio.Services;

namespace EarthModelStudio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _updateRunning;

    public HomeViewModel Home { get; }
    public DownloadViewModel Download { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>模型宿主：按清单加载模型自带的功能程序集。</summary>
    public ModelHost Models { get; }

    [ObservableProperty] private ObservableObject _current = null!;
    [ObservableProperty] private bool _isHomeActive = true;
    [ObservableProperty] private bool _isDownloadActive;
    [ObservableProperty] private bool _isSettingsActive;

    // 启动更新状态条
    [ObservableProperty] private bool _updateBarVisible;
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private string _updateDetail = "";
    [ObservableProperty] private string _launcherNotice = "";

    /// <summary>由主窗口注入的确认对话框。</summary>
    public Func<string, Task<bool>>? ConfirmHandler { get; set; }

    public event Action<ModelPackage>? PlayerRequested;

    public string VersionText => "v" + UpdateService.CurrentLauncherVersion;
    public string PlatformText => UpdateService.CurrentRid;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Models = services.ModelHost;
        Home = new HomeViewModel(services, this);
        Download = new DownloadViewModel(services, this);
        Settings = new SettingsViewModel(services);
        Current = Home;
    }

    partial void OnCurrentChanged(ObservableObject value)
    {
        IsHomeActive = value is HomeViewModel;
        IsDownloadActive = value is DownloadViewModel;
        IsSettingsActive = value is SettingsViewModel;
    }

    [RelayCommand]
    public void GoHome()
    {
        Home.Refresh();
        Current = Home;
    }

    [RelayCommand]
    public void GoDownload()
    {
        Current = Download;
        Download.OnShown();
    }

    [RelayCommand]
    public void GoSettings()
    {
        Settings.Refresh();
        Current = Settings;
    }

    /// <summary>启动模型：交给模型自己的程序集，在独立窗口中运行。</summary>
    public void PlayModel(ModelPackage package)
    {
        _services.Memory.Refresh();
        PlayerRequested?.Invoke(package);
    }

    public async Task<bool> ConfirmAsync(string message)
        => ConfirmHandler == null || await ConfirmHandler(message);

    // ---- 自动更新 ----------------------------------------------------------

    /// <summary>启动后由 App 调用：后台检查更新，不阻塞主界面。</summary>
    public Task RunStartupUpdateAsync() => RunUpdateAsync(false);

    /// <summary>状态条 / 设置页的「检查更新」，忽略自动开关强制检查。</summary>
    [RelayCommand]
    private Task CheckUpdate() => RunUpdateAsync(true);

    private async Task RunUpdateAsync(bool force)
    {
        if (_updateRunning) return;
        _updateRunning = true;
        try
        {
            UpdateBarVisible = true;
            UpdateBusy = true;
            UpdateProgress = 0;
            UpdateStatus = L.I["update.checking"];
            UpdateDetail = "";
            LauncherNotice = "";

            // 在 UI 线程构造，进度回调自动回到 UI 线程
            var progress = new Progress<UpdateProgress>(p =>
            {
                UpdateProgress = Math.Clamp(p.Value, 0, 1);
                UpdateStatus = p.Message;
                UpdateDetail = p.Detail ?? "";
            });

            var result = await _services.Updates.RunAsync(force, progress);

            UpdateStatus = result.Message;
            UpdateDetail = result.Error ?? "";
            LauncherNotice = result.LauncherMessage ?? "";

            // 模型下载完成后本次启动就刷新列表，立刻可用
            if (result.UpdatedModels > 0) Home.Refresh();

            if ((result.Outcome == UpdateOutcome.NoChange || result.Outcome == UpdateOutcome.Applied) &&
                string.IsNullOrEmpty(LauncherNotice))
                _ = AutoHideAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus = L.I["update.offline"];
            UpdateDetail = ex.Message;
        }
        finally
        {
            UpdateBusy = false;
            _updateRunning = false;
        }
    }

    private async Task AutoHideAsync()
    {
        try
        {
            await Task.Delay(6000);
            if (!_updateRunning && string.IsNullOrEmpty(LauncherNotice)) UpdateBarVisible = false;
        }
        catch { /* 忽略 */ }
    }
}
