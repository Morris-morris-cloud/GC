using System;
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

        // 界面只有 Flutter 一套（内置 Avalonia 界面已移除）。
        if (FlutterShell.TryRun(args)) return;

        // 找不到前端：给出明确提示后退出，不要静默启动一个没有界面的后端进程。
        FlutterShell.ReportMissingUi();
        Environment.Exit(1);
    }
}
