using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GC.ModelSdk;
using GC.EarthModels;

namespace GC.EarthModels.Preview;

/// <summary>
/// 封面生成工具：给每个模型画一张 preview.png。
///
/// 两条路径：
///  1) 通用路径（默认）：按模型清单的 assembly + type 加载模型程序集，
///     调它自己的 IModelRuntime.CreateView 做 headless 渲染 —— 任何新模型都能自动出封面。
///  2) 地球模型路径：仍走共享引擎的 CelestialView（保留 --bench 基准测试能力）。
///
/// 封面在模型包里，启动器只负责显示。用法：
///   dotnet run --project model/_engine/PreviewGen -- &lt;仓库根&gt; [--bench]
/// </summary>
internal static class Program
{
    private const int Width = 768;
    private const int Height = 316;

    /// <summary>headless 封面应用：带上 Fluent 主题，UserControl 才有模板可渲染。</summary>
    private sealed class PreviewApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var modelRoot = Path.Combine(root, "model");
        if (!Directory.Exists(modelRoot))
        {
            Console.Error.WriteLine($"找不到模型库: {modelRoot}");
            return 1;
        }

        var bench = args.Contains("--bench");

        AppBuilder.Configure<PreviewApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        foreach (var modelDir in Directory.GetDirectories(modelRoot).OrderBy(p => p, StringComparer.Ordinal))
        {
            var id = Path.GetFileName(modelDir);
            if (id.StartsWith('.') || id.StartsWith('_')) continue;

            var dataDir = Path.Combine(modelDir, id + ".model");
            if (!Directory.Exists(dataDir)) continue;

            var outFile = Path.Combine(dataDir, "preview.png");
            var hasScene = File.Exists(Path.Combine(dataDir, "scene.json"));

            // 基准测试只对地球模型（依赖 CelestialView）
            if (bench)
            {
                if (!hasScene) continue;
                BenchEarthModel(dataDir, id);
                continue;
            }

            var ok = false;
            string? err = null;
            if (hasScene)
                ok = TryRenderEarthModel(dataDir, outFile, out err);
            if (!ok)
                ok = TryRenderViaContract(modelDir, dataDir, outFile, out err);
            if (!ok)
                Console.Error.WriteLine($"preview 跳过 {id}：{err}");
        }

        return 0;
    }

    // ─────────────────────────────────────────── 地球模型（保留原渲染器，封面一定是真场景）

    private static bool TryRenderEarthModel(string dataDir, string outFile, out string? error)
    {
        error = null;
        try
        {
            var runtime = SceneRuntime.Load(dataDir, 2048);
            var texts = ModelStrings.Load(dataDir, "zh-CN");
            var revolution = string.Equals(runtime.Scene.DefaultView, "revolution", StringComparison.OrdinalIgnoreCase);
            var view = new CelestialView
            {
                Scene = runtime,
                Texts = texts,
                View = revolution ? SceneView.Revolution : SceneView.Rotation,
                IsPlaying = false,
                ShowHud = false,
                ShowLabels = false,
                ShowOrbit = true,
                ShowGraticule = true,
                Width = Width,
                Height = Height
            };
            PrepareView(view, Width, Height);
            view.JumpToOrbit(revolution ? 45 : 0);
            view.JumpToSpin(20);
            Save(view, outFile);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void BenchEarthModel(string dataDir, string id)
    {
        var runtime = SceneRuntime.Load(dataDir, 2048);
        var texts = ModelStrings.Load(dataDir, "zh-CN");
        var revolution = string.Equals(runtime.Scene.DefaultView, "revolution", StringComparison.OrdinalIgnoreCase);
        var view = new CelestialView
        {
            Scene = runtime,
            Texts = texts,
            View = revolution ? SceneView.Revolution : SceneView.Rotation,
            IsPlaying = false,
            ShowHud = false,
            ShowLabels = false,
            ShowOrbit = true,
            ShowGraticule = true,
            Width = Width,
            Height = Height
        };
        PrepareView(view, Width, Height);
        view.JumpToOrbit(revolution ? 45 : 0);
        view.JumpToSpin(20);

        view.Width = 1240;
        view.Height = 820;
        view.Measure(new Size(1240, 820));
        view.Arrange(new Rect(0, 0, 1240, 820));
        var animField = typeof(CelestialView)
            .GetField("_animating", BindingFlags.NonPublic | BindingFlags.Instance);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var frame = 0; frame < 20; frame++)
        {
            view.JumpToSpin(20 + frame * 3);
            using var tmp = new RenderTargetBitmap(new PixelSize(1240, 820), new Vector(96, 96));
            tmp.Render(view);
        }
        sw.Stop();
        var still = sw.Elapsed.TotalMilliseconds / 20;
        animField?.SetValue(view, true);
        sw.Restart();
        for (var frame = 0; frame < 20; frame++)
        {
            view.JumpToSpin(20 + frame * 3);
            using var tmp = new RenderTargetBitmap(new PixelSize(1240, 820), new Vector(96, 96));
            tmp.Render(view);
        }
        sw.Stop();
        Console.WriteLine($"{id}: 静止 {still:0.0} ms/frame · 播放 {sw.Elapsed.TotalMilliseconds / 20:0.0} ms/frame");
    }

    // ─────────────────────────────────────────── 通用路径：按契约加载模型自己的界面

    private sealed class PreviewContext : IModelContext
    {
        public string ModelId { get; init; } = "";
        public string ModelDirectory { get; init; } = "";
        public string DataDirectory { get; init; } = "";
        public string Language => "zh-CN";
        public int MemoryBudgetMb => 512;
        public void RequestClose() { }
        public void ReportProgress(double value, string? message = null) { }
        public void Log(string message) => Console.WriteLine("    " + message);
    }

    private static bool TryRenderViaContract(string modelDir, string dataDir, string outFile, out string? error)
    {
        error = null;
        var manifestPath = Path.Combine(modelDir, Path.GetFileName(modelDir) + ".json");
        if (!File.Exists(manifestPath)) { error = "缺少模型清单"; return false; }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            var id = Path.GetFileName(modelDir);
            var asmName = GetString(root, "assembly") ?? (id + ".dll");
            var typeName = GetString(root, "type");
            var asmPath = Path.Combine(dataDir, asmName);
            if (!File.Exists(asmPath)) { error = $"找不到功能程序集 {asmName}"; return false; }

            var alc = new AssemblyLoadContext("preview:" + id, isCollectible: true);
            alc.Resolving += (_, name) =>
            {
                // 契约与 Avalonia 一律回到默认上下文，与启动器 ModelLoadContext 一致
                if (name.Name is null) return null;
                if (name.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase)) return null;
                var shared = new[] { "GC.ModelSdk", "Avalonia", "Avalonia.Base",
                    "Avalonia.Controls", "Avalonia.Skia", "Avalonia.Themes.Fluent", "SkiaSharp", "HarfBuzzSharp" };
                if (shared.Contains(name.Name, StringComparer.OrdinalIgnoreCase)) return null;
                var candidate = Path.Combine(dataDir, name.Name + ".dll");
                return File.Exists(candidate) ? alc.LoadFromAssemblyPath(candidate) : null;
            };

            var asm = alc.LoadFromAssemblyPath(asmPath);
            Type? factoryType = null;
            if (!string.IsNullOrWhiteSpace(typeName))
                factoryType = asm.GetType(typeName!, throwOnError: false);
            factoryType ??= asm.GetTypes().FirstOrDefault(t =>
                !t.IsAbstract && !t.IsInterface && typeof(IModelFactory).IsAssignableFrom(t));
            if (factoryType == null) { error = "程序集里没有 IModelFactory 实现"; return false; }
            if (Activator.CreateInstance(factoryType) is not IModelFactory factory)
            { error = "无法创建模型入口"; return false; }

            var runtime = factory.Create();
            var ctx = new PreviewContext
            {
                ModelId = id,
                ModelDirectory = modelDir,
                DataDirectory = dataDir
            };
            runtime.Prepare(ctx);
            var view = runtime.CreateView(ctx);
            if (view == null) { error = "模型没有返回界面"; return false; }

            PrepareView(view, Width, Height);
            Save(view, outFile);
            runtime.Dispose();
            (view as IDisposable)?.Dispose();
            alc.Unload();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            return false;
        }
    }

    private static string? GetString(System.Text.Json.JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() : null;

    // ─────────────────────────────────────────── 公共

    private static void PrepareView(Control view, double w, double h)
    {
        // 封面是宽幅（768×316），模型界面按设计尺寸是 1240×820；这里把布局直接按封面
        // 尺寸 Measure/Arrange，让卡片上看到的是完整布局而不是被裁掉一角。
        view.Width = w;
        view.Height = h;
        view.Measure(new Size(w, h));
        view.Arrange(new Rect(0, 0, w, h));
    }

    private static void Save(Control view, string outFile)
    {
        // headless 下 UserControl 必须挂进 Window 才有视觉树；直接 Render(Control)
        // 会得到一张全黑的图（实测）。地球模型的裸 Control 走同一路径也正常工作。
        var win = new Window
        {
            Width = Width,
            Height = Height,
            Background = Avalonia.Media.Brushes.White,
            Content = view,
            SystemDecorations = SystemDecorations.None,
            ShowInTaskbar = false
        };
        win.Show();
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        bitmap.Render(win);
        bitmap.Save(outFile);
        win.Content = null;
        win.Close();
        Console.WriteLine($"preview -> {outFile}");
    }
}
