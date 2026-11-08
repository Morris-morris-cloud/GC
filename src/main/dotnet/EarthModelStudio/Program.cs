using System;
using Avalonia;
using EarthModelStudio.Services;

namespace EarthModelStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 运行目录：可执行文件旁 .GC/（settings.json + model/ + cache/ + update/）
        var paths = AppPaths.Discover();
        paths.EnsureCreated();

        // 上次下载的启动器新版，到这次启动才应用
        if (LauncherUpdater.TryApplyPending(paths)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
