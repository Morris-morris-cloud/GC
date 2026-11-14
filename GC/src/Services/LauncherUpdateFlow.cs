using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GC.Views;

namespace GC.Services;

/// <summary>
/// 启动器更新的完整交互流程（「检查更新」与「重新下载启动器」共用）：
///   1. 关闭主窗口与所有模型窗口；
///   2. 弹出圆角进度窗；
///   3. 下载最新版到当前目录（与旧版并列）；
///   4. 安排替换脚本、关进度窗、退出进程。
/// 失败时恢复主窗口，继续使用当前版本。
/// </summary>
public static class LauncherUpdateFlow
{
    private static bool _running;
    public static bool IsRunning => _running;

    public static async Task RunAsync(AppServices services, string title)
    {
        if (_running) return;
        _running = true;

        var desktop = Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        var progressWindow = new UpdaterWindow(title);

        try
        {
            // 1. 关闭所有窗口
            if (desktop != null) desktop.MainWindow = null;
            try { mainWindow?.Close(); } catch { }
            foreach (var w in OpenWindows().ToList())
            {
                try { w.Close(); } catch { }
            }

            // 2. 进度窗
            progressWindow.Show();
            progressWindow.Report(0, "准备更新…");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // 3. 下载
            var progress = new Progress<UpdateProgressInfo>(p =>
                progressWindow.Report(p.Value, p.Stage));

            var result = await LauncherUpdater.DownloadAsync(
                services.Paths, services.Updates.Remote, progress);

            if (result.State == UpdateRunState.Failed)
            {
                progressWindow.Report(0, "更新失败", result.Message);
                await Task.Delay(2200);
                _running = false;
                progressWindow.Close();
                RestoreMainWindow(desktop, mainWindow);
                return;
            }

            progressWindow.Report(1, $"v{result.Version} 下载完成，正在重启…");

            // 4. 安排替换（下次启动生效 / 脚本接管重启）
            var scheduled = LauncherUpdater.ScheduleApply(
                services.Paths, result.PackageName ?? "");

            if (!scheduled)
            {
                progressWindow.Report(0, "无法安排更新", "请检查安装目录是否可写");
                await Task.Delay(2200);
                _running = false;
                progressWindow.Close();
                RestoreMainWindow(desktop, mainWindow);
                return;
            }

            await Task.Delay(400);
            progressWindow.Close();
            desktop?.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"更新流程异常: {ex}");
            _running = false;
            try { progressWindow.Close(); } catch { }
            RestoreMainWindow(desktop, mainWindow);
        }
    }

    private static void RestoreMainWindow(
        IClassicDesktopStyleApplicationLifetime? desktop, Window? mainWindow)
    {
        if (desktop == null || mainWindow == null) return;
        try
        {
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"恢复主窗口失败: {ex.Message}");
        }
    }

    private static IEnumerable<Window> OpenWindows()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime d)
            yield break;
        foreach (var w in d.Windows)
        {
            if (w is UpdaterWindow) continue;
            yield return w;
        }
    }
}
