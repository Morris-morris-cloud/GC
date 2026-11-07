using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Linq;
using EarthModelStudio.Models;
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
        if (args.Length > 0 && args[0] == "--update-test")
            return RunUpdateTest(args.Length > 1 ? args[1] : FindRepoRoot());

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

    /// <summary>
    /// 离线验证自动更新完整链路：用本地目录冒充 gitcode 仓库，不访问网络、不替换真实程序。
    /// </summary>
    private static int RunUpdateTest(string repoRoot)
    {
        Console.WriteLine("=== 离线自动更新链路验证 ===");
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        L.I.Initialize("zh-CN");

        var versionFile = Path.Combine(repoRoot, "version.json");
        if (!File.Exists(versionFile))
        {
            Console.WriteLine("FAIL: 缺少 version.json，请先执行 ./build.sh stamp");
            return 1;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "ems-update-" + Guid.NewGuid().ToString("N")[..8]);
        var remote = Path.Combine(tmp, "remote");
        var home = Path.Combine(tmp, ".GC");
        var modelRoot = Path.Combine(home, "model");
        Directory.CreateDirectory(modelRoot);

        // 模拟远端仓库：version.json + 每个模型的 <id>.json / <id>.model/
        Directory.CreateDirectory(Path.Combine(remote, "model"));
        File.Copy(versionFile, Path.Combine(remote, "version.json"), true);
        foreach (var dir in Directory.GetDirectories(Path.Combine(repoRoot, "model")))
        {
            var id = Path.GetFileName(dir);
            if (id.StartsWith('_')) continue;
            var manifest = Path.Combine(dir, id + ".json");
            var data = Path.Combine(dir, id + ".model");
            if (!File.Exists(manifest) || !Directory.Exists(data)) continue;
            var dest = Path.Combine(remote, "model", id);
            Directory.CreateDirectory(dest);
            File.Copy(manifest, Path.Combine(dest, id + ".json"), true);
            CopyTree(data, Path.Combine(dest, id + ".model"));
        }

        // EMS_HOME 指向不含 .sln 的临时目录 -> 非开发态，走真实 .GC 布局
        Environment.SetEnvironmentVariable("EMS_HOME", tmp);
        Environment.SetEnvironmentVariable("EMS_DATA", home);
        Environment.SetEnvironmentVariable("EMS_MODELS", modelRoot);
        Environment.SetEnvironmentVariable("EMS_NO_SELF_UPDATE", "1");

        var paths = AppPaths.Discover();
        paths.EnsureCreated();
        var settings = new SettingsService(paths);
        settings.Load();
        var updater = new UpdateService(paths, settings, new DirectoryRemote(remote));

        var failures = 0;
        void Check(bool ok, string label)
        {
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {label}");
            if (!ok) failures++;
        }

        Console.WriteLine($"运行目录: {paths.HomeRoot}");
        Console.WriteLine($"模型库:   {paths.ModelRoot}");
        Check(paths.ModelRoot == modelRoot, ".GC/model 是下载目标");
        Check(File.Exists(paths.SettingsFile), ".GC/settings.json 已创建");

        // 1) 首次启动：模型全部下载并立即落地
        var r1 = updater.RunAsync(true, null).GetAwaiter().GetResult();
        Console.WriteLine($"首次启动: {r1.Message} 更新数={r1.UpdatedModels}");
        Check(r1.UpdatedModels == 2, "首次启动下载 2 个模型");
        foreach (var id in new[] { "earth-rotation", "earth-revolution" })
        {
            var dir = Path.Combine(modelRoot, id);
            Check(File.Exists(Path.Combine(dir, id + ".json")), id + " 清单已落地");
            Check(File.Exists(Path.Combine(dir, id + ".model", "scene.json")), id + " 场景数据已落地");
            Check(!Directory.Exists(Path.Combine(dir, "src")), id + " 源码没有下到运行目录");
            Check(settings.Current.InstalledModelVersions.ContainsKey(id), id + " 版本已记录到 settings");
        }

        // 2) 版本没变：不重复下载
        var r2 = updater.RunAsync(true, null).GetAwaiter().GetResult();
        Console.WriteLine($"二次检查: {r2.Message} 更新数={r2.UpdatedModels}");
        Check(r2.UpdatedModels == 0, "版本未变时不重复下载");

        // 3) 远端模型改版：只更新这一个，并且本次启动立即生效
        var rv = Json.Read<RepoVersion>(Path.Combine(remote, "version.json"))!;
        Check(rv.Models.Count == 2 && !string.IsNullOrWhiteSpace(rv.Models[0].Version),
            "version.json 里模型版本由构建自动生成");
        var changed = rv.Models[0];
        changed.Version += "-changed";
        var stampName = "_version-stamp.txt";
        File.WriteAllText(Path.Combine(remote, "model", changed.Id,
            changed.Id + ".model", stampName), "new content");
        Json.Write(Path.Combine(remote, "version.json"), rv);
        var r3 = updater.RunAsync(true, null).GetAwaiter().GetResult();
        Console.WriteLine($"模型改版: {r3.Message} 更新数={r3.UpdatedModels}");
        Check(r3.UpdatedModels == 1, "模型版本变化只更新 1 个模型");
        Check(File.Exists(Path.Combine(modelRoot, changed.Id, changed.Id + ".model", stampName)),
            "新版本本次启动立即生效");

        // 4) 启动器新版：下载到 update/ 并写 pending，不立刻替换程序
        var rid = UpdateService.CurrentRid;
        Console.WriteLine($"当前平台: {rid}，启动器版本: {UpdateService.CurrentLauncherVersion}");
        var stage = Path.Combine(tmp, "launcher-stage");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "EarthModelStudio.fake"), "new launcher");
        var zip = Path.Combine(tmp, "GC-" + rid + ".zip");
        ZipFile.CreateFromDirectory(stage, zip);
        rv.Launcher.Version = "9.9.9";
        rv.Launcher.Packages[rid] = new RepoPackage { Url = zip, Path = "dist/GC-" + rid + ".zip" };
        Json.Write(Path.Combine(remote, "version.json"), rv);
        var r4 = updater.RunAsync(true, null).GetAwaiter().GetResult();
        Console.WriteLine($"启动器新版: {r4.LauncherMessage}");
        Check(File.Exists(Path.Combine(paths.UpdateRoot, "pending.json")), "启动器更新写入 pending.json");
        Check(File.Exists(Path.Combine(paths.UpdateRoot, "payload", "EarthModelStudio.fake")),
            "更新包已解压到 update/payload");
        Check(settings.Current.PendingLauncherVersion == "9.9.9", "settings 记录待应用版本");
        Check((r4.LauncherMessage ?? "").Contains("下次启动"), "提示下次启动才生效");

        // 5) dry-run：验证下次启动的替换脚本能正确生成（不真的启动进程）
        Environment.SetEnvironmentVariable("EMS_NO_SELF_UPDATE", "");
        var scripted = LauncherUpdate.TryApplyPending(paths, launch: false);
        var scriptPath = Path.Combine(paths.UpdateRoot,
            OperatingSystem.IsWindows() ? "apply-update.bat" : "apply-update.sh");
        Check(scripted && File.Exists(scriptPath), "下次启动的替换脚本可生成");
        if (File.Exists(scriptPath))
        {
            var script = File.ReadAllText(scriptPath);
            Check(script.Contains(paths.InstallDirectory), "脚本包含正确的安装目录");
        }

        // 6) 断网兜底：保留本地模型，不清空
        var offline = new UpdateService(paths, settings, new GitCodeRemote("http://127.0.0.1:1/nope"));
        var beforeCount = settings.Current.InstalledModelVersions.Count;
        var r5 = offline.RunAsync(true, null).GetAwaiter().GetResult();
        Check(r5.Outcome == UpdateOutcome.Unavailable &&
              settings.Current.InstalledModelVersions.Count == beforeCount &&
              Directory.Exists(Path.Combine(modelRoot, "earth-rotation")),
            "断网时保留本地模型、不清空模型库");

        try { Directory.Delete(tmp, true); } catch { }
        Console.WriteLine(failures == 0
            ? "=== 自动更新链路全部通过 ==="
            : $"=== 有 {failures} 项失败 ===");
        return failures == 0 ? 0 : 1;
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith('.')) continue;
            var dest = Path.Combine(dst, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
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

            // 缩放 / 触控 / 教学能力：模型画布由模型程序集提供，这里用反射逐项校验
            var canvases = Descendants(player).Where(c => c.GetType().Name == "CelestialView").ToList();
            var canvas = canvases.FirstOrDefault(c => Prop(c, "InsetMode") is bool inset && !inset)
                         ?? canvases.FirstOrDefault();
            var isRevolution = packages[i].Id.Contains("revolution", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  {packages[i].Id}: 画布数={canvases.Count}（含课堂特写）");
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
                var hasMultiTouch = type.GetField("_pointers",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null;
                Console.WriteLine($"  {packages[i].Id}: 缩放={beforeZoom:0.00}→{afterZoom:0.00}→{resetZoom:0.00} 拖动={hasPointerOverride} 多指跟踪={hasMultiTouch}");

                // 双指捏合：两指间距 100→150，缩放应当 1.00→1.50
                var pinch = type.GetMethod("PinchZoom",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var pinchZoom = (double)(pinch?.Invoke(null, new object[] { 1.0, 100.0, 150.0 }) ?? 0d);
                Console.WriteLine($"  {packages[i].Id}: 捏合 100→150px = {pinchZoom:0.00}×");

                // 手动转动：竖直拖动 300px 也不能改变视角平面
                var elevBefore = Field(canvas, "_camElevTarget");
                var angleBefore = (double)(Prop(canvas, isRevolution ? "OrbitAngle" : "SpinAngle") ?? 0d);
                type.GetMethod("DragBy")?.Invoke(canvas, new object[] { 40.0, 300.0 });
                var elevAfter = Field(canvas, "_camElevTarget");
                var angleAfter = (double)(Prop(canvas, isRevolution ? "OrbitAngle" : "SpinAngle") ?? 0d);
                Console.WriteLine($"  {packages[i].Id}: 拖动 视角={elevBefore}→{elevAfter} 地球角度={angleBefore:0.0}→{angleAfter:0.0} 平面不变={Equals(elevBefore, elevAfter)}");

                if (isRevolution)
                {
                    // 两至日的直射点必须是教材值 23.5°
                    foreach (var (angle, expect) in new[] { (0d, 0d), (90d, 23.5d), (180d, 0d), (270d, -23.5d) })
                    {
                        type.GetMethod("JumpToOrbit")?.Invoke(canvas, new object[] { angle });
                        var decl = (double)(Prop(canvas, "SubsolarLatitude") ?? double.NaN);
                        Console.WriteLine($"  直射点 {angle:0}° => {decl:0.00}° （应 {expect:0.0}°，误差 {Math.Abs(decl - expect):0.000}）");
                    }

                    // 点「夏至」应自动定格，并显示 23.5°N 的课堂读数
                    var summer = Descendants(player).OfType<Button>()
                        .FirstOrDefault(b => (b.Content as TextBlock)?.Text == "夏至" && ShownInTree(b));
                    summer?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    var frozen = Prop(canvas, "IsPlaying") is bool play && !play;
                    var frozenAngle = (double)(Prop(canvas, "OrbitAngle") ?? -1d);
                    var cardReadout = Descendants(player).OfType<TextBlock>()
                        .Any(t => (t.Text ?? "") == "23.5°N" && ShownInTree(t));
                    Console.WriteLine($"  夏至按钮: 已定格={frozen} 角度={frozenAngle:0} 课堂卡显示 23.5°N={cardReadout}");
                    Capture(player, "page-player-solstice.png");
                }
            }

            // 课堂演示卡 / 底部触控提示
            var hasCard = Descendants(player).OfType<TextBlock>()
                .Any(t => (t.Text ?? "").Contains("课堂演示", StringComparison.Ordinal) && ShownInTree(t));
            var hasHint = Descendants(player).OfType<TextBlock>()
                .Any(t => (t.Text ?? "").Contains("拖动", StringComparison.Ordinal) && ShownInTree(t));
            Console.WriteLine($"  {packages[i].Id}: 课堂演示卡={hasCard} 交互提示={hasHint}");
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

    private static object? Prop(Control control, string name)
        => control.GetType().GetProperty(name)?.GetValue(control);

    private static object? Field(Control control, string name)
        => control.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(control);

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
