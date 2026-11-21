using System;

namespace GC.Services;

/// <summary>组合根：一处初始化所有服务。</summary>
public sealed class AppServices
{
    public static AppServices Current { get; private set; } = null!;

    public AppPaths Paths { get; }
    public SettingsService Settings { get; }
    public ModelRepository Models { get; }
    public IDownloadCenter Downloads { get; }
    public MemoryBudget Memory { get; }
    public IModelSourceClient Sources { get; }
    /// <summary>启动时检查模型 / 启动器更新。</summary>
    public IUpdateService Updates { get; }
    /// <summary>双源测速选择器（Xhmcl / GitCode 取最快）。</summary>
    public ISourceSelector Selector { get; }
    public ILauncherUpdateService LauncherUpdates { get; }
    public IDownloadModule DownloadModule { get; }

    private AppServices(AppPaths paths)
    {
        Paths = paths;
        Settings = new SettingsService(paths);
        Settings.Load();                         // 先读设置，才知道用在线源还是本地离线源
        Models = new ModelRepository(paths);
        var factory = DownloadModuleHost.Factory
            ?? throw new InvalidOperationException("GC-DOWNLOAD 模块尚未注册");
        DownloadModule = factory.Create(paths, Settings, Models);
        Sources = DownloadModule.Sources;
        Downloads = DownloadModule.Downloads;
        Memory = new MemoryBudget();
        Selector = DownloadModule.Selector;
        Updates = DownloadModule.Updates;
        LauncherUpdates = DownloadModule.LauncherUpdates;
    }

    public static AppServices Create()
    {
        var paths = AppPaths.Discover();
        paths.EnsureCreated();

        var services = new AppServices(paths);
        L.I.Initialize(services.Settings.Current.Language);
        services.Memory.ApplyAuto(services.Settings.Current.MemoryAuto,
                                  services.Settings.Current.MemoryMb);

        Current = services;
        return services;
    }

    public static AppServices Ensure() => Current ?? Create();
}
