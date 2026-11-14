using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GC.Services;

namespace GC.ViewModels;

/// <summary>「关于」页面：版本号 + 开发者（读取仓库 contributors）。</summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _loaded;

    public ObservableCollection<ContributorItem> Contributors { get; } = new();

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasContributors;

    public AboutViewModel(AppServices services)
    {
        _services = services;
        LoadCommand = new AsyncRelayCommand(() => LoadAsync(force: true));
    }

    public string AppName => "Morris GC";

    /// <summary>启动器版本号，如 v1.0.2。</summary>
    public string VersionText => "v" + LauncherRuntimeInfo.CurrentLauncherVersion;

    /// <summary>当前平台标识，如 win-x64 / osx-arm64。</summary>
    public string PlatformText => LauncherRuntimeInfo.CurrentRid;

    public string RepositoryText => _services.Settings.Current.UpdateRepository;

    public string ContributorsTitle => "开发者";

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>每次进入页面时调用（首次才真正联网拉取）。</summary>
    public void OnShown()
    {
        if (_loaded) return;
        _ = LoadAsync(force: false);
    }

    private async Task LoadAsync(bool force)
    {
        if (_loaded && !force) return;
        _loaded = true;

        IsLoading = true;
        StatusText = "正在读取贡献者…";
        try
        {
            var list = await ContributorsService.FetchAsync(_services.Settings.Current.UpdateRepository);
            Contributors.Clear();
            foreach (var c in list)
                Contributors.Add(new ContributorItem(c));

            HasContributors = Contributors.Count > 0;
            StatusText = HasContributors ? $"{Contributors.Count} 位贡献者" : "暂无贡献者信息";
        }
        catch (Exception ex)
        {
            StatusText = "读取失败：" + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenRepository() => Shell.OpenUrl(RepositoryText);

    /// <summary>重新下载启动器：关闭所有窗口，进度窗下载新版，完成后重启。</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ReinstallLauncher()
    {
        if (LauncherUpdateFlow.IsRunning) return;
        await LauncherUpdateFlow.RunAsync(_services, "正在重新下载启动器");
    }
}

/// <summary>一位贡献者的展示项。</summary>
public sealed class ContributorItem
{
    public ContributorItem(Contributor c)
    {
        Name = c.DisplayName;
        Contributions = c.Contributions;
        AvatarUrl = c.AvatarUrl;
    }

    public string Name { get; }
    public int Contributions { get; }
    public string? AvatarUrl { get; }
    public string ContributionsText => $"{Contributions} 次提交";
    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarUrl);
}
