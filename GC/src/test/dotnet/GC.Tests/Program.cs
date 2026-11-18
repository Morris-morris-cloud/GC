using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GC.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GC;
using GC.Download;
using GC.Services;
using GC.ViewModels;
using GC.Views;

namespace GC.Tests;

/// <summary>
/// 离屏校验工具：把启动器页面和模型窗口渲染成 PNG。
/// 用法：dotnet run --project GC/src/test/dotnet/GC.Tests -- build/screenshots
/// </summary>
internal static class Program
{
    private static string _outDir = "screenshots";

    [STAThread]
    public static int Main(string[] args)
    {
        DownloadModuleHost.Factory = new DownloadModuleFactory();
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
        var fixtureVersion = Json.Read<RepoVersion>(versionFile)!;
        fixtureVersion.Models.RemoveAll(model =>
            model.Id is not "earth-rotation" and not "earth-revolution");
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

        // 按远端实际内容重算完整性哈希（与 VersionStamp 同一算法），然后签名。
        // 客户端会复算这两项，不一致就拒绝安装 —— 这一步必须与构建端一致。
        foreach (var m in fixtureVersion.Models)
        {
            var computed = ModelIntegrity.Compute(Path.Combine(remote, "model", m.Id), m.Id);
            m.ManifestSha256 = computed.ManifestSha256;
            m.FilesSha256 = computed.FilesSha256;
            m.Files = computed.Files.Count;
        }

        var signer = ManifestSignature.TryLoadPrivateKey(
            Path.Combine(repoRoot, ".signing", "gc-signing.pem"));
        if (signer == null)
        {
            Console.WriteLine("FAIL: 缺少签名私钥 .signing/gc-signing.pem，无法构造可信清单");
            return 1;
        }

        var remoteManifest = Path.Combine(remote, "version.json");
        WriteSigned(remoteManifest, fixtureVersion, signer);
        Console.WriteLine($"远端清单已签名（{ManifestSignature.Algorithm}）");

        // EMS_HOME 指向不含 .sln 的临时目录 -> 非开发态，走真实 .GC 布局
        Environment.SetEnvironmentVariable("EMS_HOME", tmp);
        Environment.SetEnvironmentVariable("EMS_DATA", home);
        Environment.SetEnvironmentVariable("EMS_MODELS", null);
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
        Check(paths.ModelRoot == modelRoot, "模型安装目录位于 .GC/model");
        Check(paths.CacheRoot.StartsWith(paths.HomeRoot, StringComparison.Ordinal), "模型与启动器下载暂存位于 .GC/cache");
        Check(paths.UpdateRoot.StartsWith(paths.HomeRoot, StringComparison.Ordinal), "启动器更新位于 .GC/update");
        Check(File.Exists(paths.SettingsFile), ".GC/config/settings.json 已创建");

        // 1) 首次启动：模型全部下载并立即落地
        var r1 = Task.Run(() => updater.RunAsync(true, null)).GetAwaiter().GetResult();
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
        var r2 = Task.Run(() => updater.RunAsync(true, null)).GetAwaiter().GetResult();
        Console.WriteLine($"二次检查: {r2.Message} 更新数={r2.UpdatedModels}");
        Check(r2.UpdatedModels == 0, "版本未变时不重复下载");

        // 3) 远端模型改版：只更新这一个，并且本次启动立即生效
        var rv = Json.Read<RepoVersion>(remoteManifest)!;
        Check(rv.Models.Count == 2 && !string.IsNullOrWhiteSpace(rv.Models[0].Version),
            "version.json 里模型版本由构建自动生成");
        var changed = rv.Models[0];
        changed.Version += "-changed";
        var stampName = "_version-stamp.txt";
        File.WriteAllText(Path.Combine(remote, "model", changed.Id,
            changed.Id + ".model", stampName), "new content");

        // 内容变了 → 指纹必须跟着变，并且清单要重新签名，否则客户端会（正确地）拒绝
        var changedHash = ModelIntegrity.Compute(Path.Combine(remote, "model", changed.Id), changed.Id);
        changed.ManifestSha256 = changedHash.ManifestSha256;
        changed.FilesSha256 = changedHash.FilesSha256;
        changed.Files = changedHash.Files.Count;
        WriteSigned(remoteManifest, rv, signer);

        var r3 = Task.Run(() => updater.RunAsync(true, null)).GetAwaiter().GetResult();
        Console.WriteLine($"模型改版: {r3.Message} 更新数={r3.UpdatedModels}");
        Check(r3.UpdatedModels == 1, "模型版本变化只更新 1 个模型");
        Check(File.Exists(Path.Combine(modelRoot, changed.Id, changed.Id + ".model", stampName)),
            "新版本本次启动立即生效");
        Check(File.Exists(Path.Combine(modelRoot, changed.Id, changed.Id + ".integrity.json")),
            "安装后写入完整性记录");

        // 4) 启动器新版：用 LauncherUpdater 下载到当前目录（与旧版并列）
        var rid = LauncherRuntimeInfo.CurrentRid;
        Console.WriteLine($"当前平台: {rid}，启动器版本: {LauncherRuntimeInfo.CurrentLauncherVersion}");
        var stage = Path.Combine(tmp, "launcher-stage");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "GC-9.9.9-" + LauncherUpdater.PlatformLabel + ".exe"),
            "new launcher");
        var zip = Path.Combine(tmp, "GC-" + rid + ".zip");
        ZipFile.CreateFromDirectory(stage, zip);
        rv.Launcher.Version = "9.9.9";
        rv.Launcher.Packages[rid] = new RepoPackage
        {
            Url = zip,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(),
            Size = new FileInfo(zip).Length
        };
        WriteSigned(remoteManifest, rv, signer);

        // 造一个旧版在当前目录
        var oldName = "GC-1.0.0-" + LauncherUpdater.PlatformLabel + ".exe";
        File.WriteAllText(Path.Combine(paths.InstallDirectory, oldName), "old launcher");

        var dl = Task.Run(() => LauncherUpdater.DownloadAsync(paths, updater.Remote, null))
            .GetAwaiter().GetResult();
        Console.WriteLine($"启动器下载: {dl.Message}");
        Check(dl.State == UpdateRunState.Ready, "启动器下载成功");
        Check(!string.IsNullOrWhiteSpace(dl.PackageName), "得到新版包名");
                Check(File.Exists(Path.Combine(paths.UpdateRoot, "payload", dl.PackageName ?? "")) ||
                            Directory.Exists(Path.Combine(paths.UpdateRoot, "payload", dl.PackageName ?? "")),
                        "新版暂存于 .GC/update/payload");
                Check(!File.Exists(Path.Combine(paths.InstallDirectory, dl.PackageName ?? "")) &&
                            !Directory.Exists(Path.Combine(paths.InstallDirectory, dl.PackageName ?? "")),
                        "下载阶段不会把新版写入安装目录");
        Check(File.Exists(Path.Combine(paths.InstallDirectory, oldName)),
            "旧版仍在（删除由替换脚本在重启后做）");

        // 5) 校验标记文件与替换脚本（相对路径，无绝对路径）
        var marker = Path.Combine(paths.UpdateRoot, "update.txt");
        Check(File.Exists(marker), "写出更新标记 update.txt");
        var scheduled = LauncherUpdater.ScheduleApply(paths, dl.PackageName ?? "");
        var scriptPath = Path.Combine(paths.UpdateRoot,
            OperatingSystem.IsWindows() ? "apply.bat" : "apply.sh");
        Check(scheduled && File.Exists(scriptPath), "替换脚本可生成");
        if (File.Exists(scriptPath))
        {
            var script = File.ReadAllText(scriptPath);
            Check(!script.Contains(tmp), "脚本不含绝对路径");
            Check(script.Contains("GC-*.app") || script.Contains("GC-*"), "脚本按 GC-* 匹配删除");
        }

        // 5b) 只发产物、不发 Release：更新包只存在于仓库 dist/<系统>-<架构>/ 目录里。
        //     清单不写 url、不写 path，只给 dir + name + sha256，客户端要能自己找过去。
        var distDir = Path.Combine(remote, "dist", LauncherUpdater.PlatformLabel);
        Directory.CreateDirectory(distDir);
        var distName = "GC-9.9.10-" + LauncherUpdater.PlatformLabel + ".zip";
        var distZip = Path.Combine(distDir, distName);
        File.Copy(zip, distZip, true);

        string Sha(string file) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

        rv.Launcher.Version = "9.9.10";
        rv.Launcher.Packages[rid] = new RepoPackage
        {
            Dir = "dist/" + LauncherUpdater.PlatformLabel,
            Name = distName,
            Sha256 = Sha(distZip),
            Size = new FileInfo(distZip).Length
        };
        WriteSigned(remoteManifest, rv, signer);

        var dlDist = Task.Run(() => LauncherUpdater.DownloadAsync(paths, updater.Remote, null))
            .GetAwaiter().GetResult();
        Console.WriteLine($"dist 目录更新: {dlDist.Message}");
        Check(dlDist.State == UpdateRunState.Ready, "仓库 dist/<系统>-<架构>/ 里的产物能找到并下载");
        // 包名来自 zip 内容（这里仍是 9.9.9 的文件），所以要断言清单版本
        Check(dlDist.Version == "9.9.10", "下载的确实是 dist 目录里声明的那个版本");

        // 5c) 往 dist/ 里塞一个哈希不匹配的包：必须拒绝（sha256 来自已签名清单）
        File.WriteAllText(distZip, "evil payload");
        var dlEvil = Task.Run(() => LauncherUpdater.DownloadAsync(paths, updater.Remote, null))
            .GetAwaiter().GetResult();
        Check(dlEvil.State == UpdateRunState.Failed, "dist 目录里哈希不符的包被拒绝");

        // 还原到 9.9.9，后面的篡改 / 断网用例继续用这份清单
        rv.Launcher.Version = "9.9.9";
        rv.Launcher.Packages[rid] = new RepoPackage
        {
            Url = zip,
            Sha256 = Sha(zip),
            Size = new FileInfo(zip).Length
        };
        WriteSigned(remoteManifest, rv, signer);

        // 6) 篡改检测：改一个字节但不重新签名 -> 必须判为不可信
        var tampered = File.ReadAllText(remoteManifest).Replace("\"9.9.9\"", "\"9.9.8\"");
        var tamperResult = ManifestSignature.Verify(tampered, requireSignature: true);
        Check(tamperResult.Trust == ManifestTrust.Invalid, "被篡改的清单验签失败");
        File.WriteAllText(remoteManifest, tampered);
        var r6 = Task.Run(() => updater.RunAsync(true, null)).GetAwaiter().GetResult();
        Check(r6.Outcome == UpdateOutcome.Unavailable, "清单被篡改时拒绝更新");
        WriteSigned(remoteManifest, rv, signer);

        // 7) 断网兜底：保留本地模型，不清空
        var offline = new UpdateService(paths, settings, new GitCodeRemote("http://127.0.0.1:1/nope"));
        var beforeCount = settings.Current.InstalledModelVersions.Count;
        var r5 = Task.Run(() => offline.RunAsync(true, null)).GetAwaiter().GetResult();
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

    /// <summary>序列化清单并用私钥签名（测试里模拟一次正式发布）。</summary>
    private static void WriteSigned(string path, RepoVersion version, ECDsa key)
    {
        // keyId 必须与 .signing/ 里的私钥对应的公钥条目一致，否则客户端按 keyId
        // 查不到受信任公钥，会判成 Invalid 而不是 Verified。
        var text = JsonSerializer.Serialize(version, Json.Options) + "\n";
        File.WriteAllText(path, ManifestSignature.SignDocument(text, key, "gc-2026-12"));
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
            if (File.Exists(Path.Combine(dir.FullName, "GC.sln"))) return dir.FullName;
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
