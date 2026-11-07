using System;

namespace EarthModelStudio.Services;

/// <summary>
/// 启动时运行内存分配（类似启动器的 -Xmx）。
/// 启动器只负责把这份预算通过 <c>IModelContext.MemoryBudgetMb</c> 交给模型，
/// 具体怎么用（贴图分辨率、缓存大小）由模型自己决定。
/// </summary>
public sealed class MemoryBudget
{
    public int BudgetMb { get; private set; } = 2048;
    public long BudgetBytes => (long)BudgetMb * 1024L * 1024L;
    public long ManagedBytes => GC.GetTotalMemory(false);

    public event Action? Changed;

    public void Apply(int mb)
    {
        BudgetMb = Math.Clamp(mb, 256, 16384);
        Changed?.Invoke();
    }

    /// <summary>内存情况变化（例如模型窗口开关）时刷新展示。</summary>
    public void Refresh() => Changed?.Invoke();

    public double UsageRatio => BudgetBytes <= 0 ? 0 : Math.Clamp((double)ManagedBytes / BudgetBytes, 0, 1);

    public string UsageText => $"{ModelScanner.HumanSize(ManagedBytes)} / {BudgetMb} MB";
}
