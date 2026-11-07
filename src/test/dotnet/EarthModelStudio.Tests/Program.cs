using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EarthModelStudio;
using EarthModelStudio.Services;
using EarthModelStudio.ViewModels;
using EarthModelStudio.Views;

namespace EarthModelStudio.Tests;

/// <summary>
/// 离屏校验工具：把启动器页面和模型窗口渲染成 PNG。
/// 用法：dotnet run --project src/test/dotnet/EarthModelStudio.Tests -- build/screenshots
/// </summary>
internal static class Program
{
    private static string _outDir = "screenshots";

    [STAThread]
    public static int Main(string[] args)
    {
        _outDir = args.Length > 0 ? args[0] : "screenshots";
        Directory.CreateDirectory(_outDir);
        Environment.SetEnvironmentVariable("EMS_HOME", FindRepoRoot());
        Environment.SetEnvironmentVariable("EMS_DATA", Path.Combine(_outDir, ".data"));

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var services = AppServices.Create();
        RenderPages(services);

        Console.WriteLine("截图输出: " + Path.GetFullPath(_outDir));
        return 0;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EarthModelStudio.sln"))) return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private static void RenderPages(AppServices services)
    {
        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(window, "page-home.png");

        vm.GoDownloadCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "page-download.png");

        vm.GoSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "page-settings.png");

        // 启动进度窗（单独截图）
        var demoLoading = new LoadingWindow("地球自转");
        demoLoading.Show(window);
        demoLoading.Report(0.42, "生成地表纹理");
        Dispatcher.UIThread.RunJobs();
        Capture(demoLoading, "page-loading.png");
        demoLoading.Close();

        var packages = services.Models.Scan();
        Console.WriteLine($"模型数: {packages.Count}");
        foreach (var p in packages)
            Console.WriteLine($"  {p.Id}: 程序集={Path.GetFileName(p.AssemblyPath)} 存在={File.Exists(p.AssemblyPath)} 封面={File.Exists(p.PreviewPath)}");

        // 启动模型：应当新开窗口，主窗口保持原页面
        for (var i = 0; i < packages.Count && i < 2; i++)
        {
            var before = window.PlayerWindows.Count;
            vm.PlayModel(packages[i]);
            // 加载是异步的（带进度窗），这里泵一下调度器等它完成
            var appeared = WaitFor(() => window.PlayerWindows.Count > before);
            var player = window.PlayerWindows.LastOrDefault();
            Console.WriteLine($"player windows = {window.PlayerWindows.Count} ({packages[i].Id}) appeared={appeared}");
            if (player == null) continue;

            if (i == 0)
            {
                Dispatcher.UIThread.RunJobs();
                Capture(player, "page-player.png");
            }
            else
            {
                Capture(player, "page-player-rotation.png");
            }

            // 节气定位 / 视图切换只属于公转模型；自转模型不该出现
            var hasSeasons = Descendants(player).OfType<TextBlock>()
                .Any(t => (t.Text ?? "").Contains("节气定位", StringComparison.Ordinal) && ShownInTree(t));
            var hasViewSwitch = Descendants(player).OfType<Button>()
                .Any(b => b.Tag is string tag && (tag == "rotation" || tag == "revolution") && ShownInTree(b));
            Console.WriteLine($"  {packages[i].Id}: 节气定位={hasSeasons} 视图切换={hasViewSwitch}");

            // 缩放 / 拖动能力：模型画布由模型程序集提供，这里用反射验证接口存在且可用
            var canvas = Descendants(player).FirstOrDefault(c => c.GetType().Name == "CelestialView");
            if (canvas != null)
            {
                var type = canvas.GetType();
                var zoomProp = type.GetProperty("Zoom");
                var zoomBy = type.GetMethod("ZoomBy");
                var resetView = type.GetMethod("ResetView");
                var beforeZoom = (double)(zoomProp?.GetValue(canvas) ?? 0d);
                zoomBy?.Invoke(canvas, new object[] { 1.25 });
                var afterZoom = (double)(zoomProp?.GetValue(canvas) ?? 0d);
                resetView?.Invoke(canvas, null);
                var resetZoom = (double)(zoomProp?.GetValue(canvas) ?? 0d);
                var hasPointerOverride = type.GetMethod("OnPointerMoved",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null;
                Console.WriteLine($"  {packages[i].Id}: 缩放={beforeZoom:0.00}→{afterZoom:0.00}→{resetZoom:0.00} 暂停拖动={hasPointerOverride}");
            }

            // 底部提示：滚轮缩放 / 暂停后可拖动
            var hasHint = Descendants(player).OfType<TextBlock>()
                .Any(t => (t.Text ?? "").Contains("拖动", StringComparison.Ordinal) && ShownInTree(t));
            Console.WriteLine($"  {packages[i].Id}: 交互提示={hasHint}");
        }

        // 关掉第二个窗口，只留一个继续验证主窗口
        var second = window.PlayerWindows.LastOrDefault();
        if (second != null) second.Close();
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine($"window after closing second = {window.PlayerWindows.Count}");

        // 主窗口仍停留在设置页 —— 说明没有被模型画面覆盖
        Capture(window, "page-main-while-playing.png");

        foreach (var player in window.PlayerWindows.ToList()) player.Close();
        Dispatcher.UIThread.RunJobs();

        window.Close();
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 20000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
            Thread.Sleep(15);
        }
        return condition();
    }

    private static bool ShownInTree(Control control)
    {
        Visual? current = control;
        while (current != null)
        {
            if (current is Control c && !c.IsVisible) return false;
            current = current.GetVisualParent();
        }
        return true;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            switch (current)
            {
                case Panel panel:
                    foreach (var child in panel.Children) stack.Push(child);
                    break;
                case Border { Child: Control only }:
                    stack.Push(only);
                    break;
                case ScrollViewer { Content: Control scrolled }:
                    stack.Push(scrolled);
                    break;
                case ContentControl { Content: Control content }:
                    stack.Push(content);
                    break;
            }
        }
    }

    private static void Capture(Window window, string fileName)
    {
        try
        {
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
            {
                Console.WriteLine("capture failed: " + fileName);
                return;
            }
            var path = Path.Combine(_outDir, fileName);
            frame.Save(path);
            Console.WriteLine($"page -> {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"capture error {fileName}: {ex.Message}");
        }
    }
}
