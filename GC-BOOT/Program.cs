using System;
using Avalonia;
using GC.Services;
using GC.Download;

namespace GC;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DownloadModuleHost.Factory = new DownloadModuleFactory();
        // 运行目录：可执行文件旁 .GC/（settings.json + model/ + cache/ + update/）
        var paths = AppPaths.Discover();
        paths.EnsureCreated();

        // 目录约定 + 双语日志 + 把包里的守护进程落到 .GC/safedog
        RuntimeLayout.Ensure(paths);

        // 上次下载的启动器新版，到这次启动才应用
        if (DownloadModuleHost.Factory.TryApplyPending(paths)) return;

        // 发布包带 Flutter 前端时，界面交给它；没带则回退到内置 Avalonia 界面。
        if (FlutterShell.TryRun(args)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
