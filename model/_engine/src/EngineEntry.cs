using System;
using Avalonia.Controls;
using GC.ModelSdk;

namespace GC.EarthModels;

/// <summary>
/// 地球模型共享运行时：读自己目录下的 scene.json / land.json / i18n，
/// 然后把自带界面交给启动器。启动器完全不知道“地球”“节气”这些东西。
/// </summary>
public sealed class EarthModelRuntime : IModelRuntime
{
    public const int SupportedApiVersion = 1;

    private SceneRuntime? _scene;

    /// <summary>后台准备：读场景、生成地表纹理（耗时都花在这里，进度条能实时走）。</summary>
    public void Prepare(IModelContext context)
    {
        context.ReportProgress(0.03, "读取场景数据");
        _scene = SceneRuntime.Load(context.DataDirectory, context.MemoryBudgetMb, context.ReportProgress);
        context.Log($"[{context.ModelId}] 场景已加载：{_scene.Scene.DefaultView}，" +
                    $"地表纹理 {(_scene.Texture == null ? "无" : _scene.Texture.Width + "×" + _scene.Texture.Height)}");
    }

    /// <summary>UI 线程构建界面（很快）。</summary>
    public Control CreateView(IModelContext context)
    {
        context.ReportProgress(0.78, "构建界面");
        _scene ??= SceneRuntime.Load(context.DataDirectory, context.MemoryBudgetMb, context.ReportProgress);
        var view = new EarthModelView(context, _scene);
        context.ReportProgress(1.0, "准备就绪");
        return view;
    }

    /// <summary>
    /// 释放预烘焙纹理（约 3MB 托管数组）与场景引用。窗口关闭时由启动器调用，
    /// 避免 collectible ALC 里的类型被长生命周期引用拖住而无法卸载。
    /// </summary>
    public void Dispose()
    {
        _scene = null;
        System.GC.SuppressFinalize(this);
    }
}
