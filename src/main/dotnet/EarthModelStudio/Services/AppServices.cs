using System;

namespace EarthModelStudio.Services;

/// <summary>组合根：一处初始化所有服务。</summary>
public sealed class AppServices
{
    public static AppServices Current { get; private set; } = null!;

    public AppPaths Paths { get; }
    public SettingsService Settings { get; }
    public ModelRepository Models { get; }
    public DownloadCenter Downloads { get; }
    public MemoryBudget Memory { get; }
    public ModelSourceClient Sources { get; }
    /// <summary>模型加载器：只负责把模型程序集跑起来，不渲染。</summary>
    public ModelHost ModelHost { get; }
    /// <summary>启动时检查模型 / 启动器更新。</summary>
    public UpdateService Updates { get; }
    /// <summary>双源测速选择器（Xhmcl / GitCode 取最快）。</summary>
    public SourceSelector Selector { get; }

    private AppServices(AppPaths paths)
    {
        Paths = paths;
        Settings = new SettingsService(paths);
        Settings.Load();                         // 先读设置，才知道用在线源还是本地离线源
        Models = new ModelRepository(paths);
        Sources = new ModelSourceClient(paths);
        Downloads = new DownloadCenter(paths, Models);
        Memory = new MemoryBudget();
        ModelHost = new ModelHost(paths, Settings, Memory);
        Selector = new SourceSelector();
        Updates = new UpdateService(paths, Settings, UpdateService.CreateRemote(Settings.Current));
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
