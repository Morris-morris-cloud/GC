using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using NeoRF.EarthModels;

namespace NeoRF.EarthModels.Preview;

/// <summary>
/// 封面生成工具：用模型自己的渲染器给每个模型画一张封面 preview.png。
/// 封面在模型包里，启动器只负责显示 —— 所以公转模型的封面一定就是公转场景。
/// 用法：dotnet run --project model-src/tools/PreviewGen -- &lt;仓库根&gt;
/// </summary>
internal static class Program
{
    private const int Width = 768;
    private const int Height = 316;

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

        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        foreach (var modelDir in Directory.GetDirectories(modelRoot))
        {
            var id = Path.GetFileName(modelDir);
            if (id.StartsWith(".")) continue;
            var dataDir = Path.Combine(modelDir, id + ".model");
            var sceneFile = Path.Combine(dataDir, "scene.json");
            if (!File.Exists(sceneFile)) continue;

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
            view.Measure(new Size(Width, Height));
            view.Arrange(new Rect(0, 0, Width, Height));
            view.JumpToOrbit(revolution ? 45 : 0);
            view.JumpToSpin(20);

            if (bench)
            {
                // 真实窗口尺寸下的帧耗时
                view.Width = 1240;
                view.Height = 820;
                view.Measure(new Size(1240, 820));
                view.Arrange(new Rect(0, 0, 1240, 820));
                // 模拟播放态（运动时降分辨率），分别测静止高清帧与运动帧
                var animField = typeof(NeoRF.EarthModels.CelestialView)
                    .GetField("_animating", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
                continue;
            }

            var outFile = Path.Combine(dataDir, "preview.png");
            using (var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96)))
            {
                bitmap.Render(view);
                bitmap.Save(outFile);
            }
            Console.WriteLine($"preview -> {outFile}");
        }

        return 0;
    }
}
