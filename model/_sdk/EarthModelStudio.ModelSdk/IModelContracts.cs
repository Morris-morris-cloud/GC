using System;
using Avalonia.Controls;

namespace EarthModelStudio.ModelSdk;

/// <summary>
/// 模型入口契约。模型程序集里必须有一个公开、无参构造的 <see cref="IModelFactory"/> 实现，
/// 启动器按模型清单里的 <c>assembly</c> + <c>type</c> 定位并创建它。
/// </summary>
public interface IModelFactory
{
    /// <summary>契约版本。启动器目前支持 1。</summary>
    int ApiVersion { get; }

    /// <summary>创建一个模型运行时实例（每个窗口一个实例）。</summary>
    IModelRuntime Create();
}

/// <summary>
/// 模型运行时：负责把模型自己的界面交给启动器。
/// 启动器只负责开窗口，**不参与任何渲染**。
/// </summary>
public interface IModelRuntime : IDisposable
{
    /// <summary>
    /// 在**后台线程**准备数据（读文件、算纹理等），期间禁止创建或操作任何 UI 控件。
    /// 启动器的进度窗会显示 <see cref="IModelContext.ReportProgress"/> 汇报的进度。
    /// 默认空实现：模型也可以把准备工作留到 CreateView 里（那样会占用 UI 线程）。
    /// </summary>
    void Prepare(IModelContext context) { }

    /// <summary>
    /// 在 UI 线程创建模型窗口的内容。模型可以自由组合控件、自己画图、自己管动画。
    /// 返回的控件会被直接放进窗口，铺满客户区。
    /// </summary>
    Control CreateView(IModelContext context);
}

/// <summary>启动器提供给模型的环境信息与能力。</summary>
public interface IModelContext
{
    /// <summary>模型 id（即模型目录名）。</summary>
    string ModelId { get; }

    /// <summary>模型目录：<c>model/&lt;id&gt;</c>。</summary>
    string ModelDirectory { get; }

    /// <summary>模型数据目录：<c>model/&lt;id&gt;/&lt;id&gt;.model</c>，模型自己的场景数据、贴图、语言包都放这里。</summary>
    string DataDirectory { get; }

    /// <summary>界面语言（zh-CN / en）。模型应按它选择自己的文案。</summary>
    string Language { get; }

    /// <summary>用户在设置里分配的运行内存预算（MB），模型可据此控制缓存规模。</summary>
    int MemoryBudgetMb { get; }

    /// <summary>请求关闭承载此模型的窗口。</summary>
    void RequestClose();

    /// <summary>
    /// 汇报加载进度（0–1）与当前阶段文案。启动器的加载进度条会实时显示，
    /// 也可以在任何时候调用（例如切换数据集时）。
    /// </summary>
    void ReportProgress(double value, string? message = null);

    /// <summary>输出日志（启动器会写到控制台）。</summary>
    void Log(string message);
}
