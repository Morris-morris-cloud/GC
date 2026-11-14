using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GC.Models;
using GC.Services;

namespace GC.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _updateRunning;

    public HomeViewModel Home { get; }
    public DownloadViewModel Download { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    /// <summary>模型宿主：按清单加载模型自带的功能程序集。</summary>
    public ModelHost Models { get; }

    [ObservableProperty] private ObservableObject _current = null!;
    [ObservableProperty] private bool _isHomeActive = true;
    [ObservableProperty] private bool _isDownloadActive;
    [ObservableProperty] private bool _isSettingsActive;
    [ObservableProperty] private bool _isAboutActive;

    // 启动更新状态条
    [ObservableProperty] private bool _updateBarVisible;
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private string _updateDetail = "";
    [ObservableProperty] private string _launcherNotice = "";

    /// <summary>由主窗口注入的确认对话框。</summary>
    public Func<string, Task<bool>>? ConfirmHandler { get; set; }

    /// <summary>
    /// 由主窗口注入的「发现启动器新版本」对话框。
    /// 参数为新版本号，返回 true 表示用户选择更新。
    /// </summary>
    public Func<string, Task<bool>>? LauncherUpdateHandler { get; set; }

    public event Action<ModelPackage>? PlayerRequested;

    public string VersionText => "v" + LauncherRuntimeInfo.CurrentLauncherVersion;
    public string PlatformText => LauncherRuntimeInfo.CurrentRid;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Models = services.ModelHost;
        Home = new HomeViewModel(services, this);
        Download = new DownloadViewModel(services, this);
        Settings = new SettingsViewModel(services);
        About = new AboutViewModel(services);
        Current = Home;
    }

    partial void OnCurrentChanged(ObservableObject value)
    {
        IsHomeActive = value is HomeViewModel;
        IsDownloadActive = value is DownloadViewModel;
        IsSettingsActive = value is SettingsViewModel;
        IsAboutActive = value is AboutViewModel;
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

    [RelayCommand]
    public void GoAbout()
    {
        About.OnShown();
        Current = About;
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

            // 双源测速：Xhmcl / GitCode 取快的那个（第 5 项）。
            // 启动更新与下载页共用同一个选择器，保证两处用同一个源、同一种下载方式。
            UpdateStatus = "正在选择最快的下载源…";
            try
            {
                var fastest = await _services.Selector.PickFastestAsync(
                    force, default);
                _services.Updates.UseRemote(_services.DownloadModule.CreateRemote(fastest));
                var lat = _services.Selector.LastLatency;
                UpdateDetail = "源：" + fastest.Name +
                    (lat.Count > 0 ? "（" + string.Join("，", lat.Select(kv =>
                        $"{SourcePreset.Find(kv.Key)?.Name ?? kv.Key} {kv.Value}ms")) + "）" : "");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[update] 测速失败，沿用当前源: {ex.Message}");
            }

            // 启动器：读 Release latest 版本号，与当前运行版本不同就弹窗询问
            var latest = await _services.LauncherUpdates.CheckAsync(_services.Updates.Remote);
            if (latest != null)
            {
                var agreed = LauncherUpdateHandler != null &&
                             await LauncherUpdateHandler(latest);
                if (agreed)
                {
                    // 走完整更新流程（关窗 -> 进度窗下载 -> 重启）
                    UpdateBusy = false;
                    _updateRunning = false;
                    UpdateBarVisible = false;
                    await LauncherUpdateFlow.RunAsync(_services, "正在更新启动器");
                    return;
                }

                UpdateStatus = $"启动器有新版本 v{latest}（已跳过）";
                UpdateDetail = "点击「检查更新」可再次更新，模型仍会同步到最新";
            }

            // 模型：同步到最新版
            var result = await _services.Updates.RunAsync(force, progress, default);

            UpdateStatus = result.Message;
            UpdateDetail = result.Error ?? "";
            LauncherNotice = result.LauncherMessage ?? "";

            // 首次启动 + 断网：本地一个模型都没有，这时候不能只留一个空页面。
            // 明确告诉用户「没坏、可以怎么办」，并保留状态条（不自动隐藏）。
            if (result.Outcome == UpdateOutcome.Unavailable && Home.Models.Count == 0)
            {
                UpdateStatus = "暂时连不上模型源，模型库为空";
                UpdateDetail = "可以到「下载」页点重试，或把模型文件夹放进 .GC/model/ 后重新打开";
                return;
            }

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
