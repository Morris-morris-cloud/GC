using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthModelStudio.Models;
using EarthModelStudio.Services;
using EarthModelStudio.ViewModels;

namespace EarthModelStudio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    public HomeViewModel Home { get; }
    public DownloadViewModel Download { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>模型宿主：按清单加载模型自带的功能程序集。</summary>
    public ModelHost Models { get; }

    [ObservableProperty] private ObservableObject _current = null!;
    [ObservableProperty] private bool _isHomeActive = true;
    [ObservableProperty] private bool _isDownloadActive;
    [ObservableProperty] private bool _isSettingsActive;

    /// <summary>由主窗口注入的确认对话框。</summary>
    public Func<string, Task<bool>>? ConfirmHandler { get; set; }

    /// <summary>
    /// 请求为新模型开一个独立窗口。真正创建窗口的是主窗口，
    /// 因此同一个模型可以同时开多份，主界面也不会被替换。
    /// </summary>
    public event Action<ModelPackage>? PlayerRequested;

    public string VersionText => "v1.0.0";
    public string PlatformText => RuntimeInformation.RuntimeIdentifier;

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
}
