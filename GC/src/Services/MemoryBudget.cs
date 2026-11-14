using System;

namespace GC.Services;

/// <summary>
/// 启动时运行内存分配（类似启动器的 -Xmx）。
/// 启动器只负责把这份预算通过 <c>IModelContext.MemoryBudgetMb</c> 交给模型，
/// 具体怎么用（贴图分辨率、缓存大小）由模型自己决定。
/// </summary>
public sealed class MemoryBudget
{
    public int BudgetMb { get; private set; } = 2048;
    public long BudgetBytes => (long)BudgetMb * 1024L * 1024L;
    public long ManagedBytes => System.GC.GetTotalMemory(false);

    public event Action? Changed;

    public void Apply(int mb)
    {
        BudgetMb = Math.Clamp(mb, 256, 16384);
        Changed?.Invoke();
    }

    /// <summary>
    /// 自动预算：取设备可用物理内存的一部分（约 1/4），
    /// 夹在 512–8192 MB 之间，低配设备也不会因为预算过大而卡顿。
    /// </summary>
    public static int AutoComputeMb()
    {
        try
        {
            var info = System.GC.GetGCMemoryInfo();
            var totalMb = info.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
            if (totalMb > 0)
                return (int)Math.Clamp(Math.Round(totalMb / 4.0 / 256.0) * 256, 512, 8192);
        }
        catch { /* 平台不支持时回退默认值 */ }
        return 2048;
    }

    /// <summary>按设置应用：auto 时自动计算，否则用手动值。</summary>
    public void ApplyAuto(bool auto, int manualMb)
        => Apply(auto ? AutoComputeMb() : manualMb);

    /// <summary>内存情况变化（例如模型窗口开关）时刷新展示。</summary>
    public void Refresh() => Changed?.Invoke();

    public double UsageRatio => BudgetBytes <= 0 ? 0 : Math.Clamp((double)ManagedBytes / BudgetBytes, 0, 1);

    public string UsageText => $"{ModelScanner.HumanSize(ManagedBytes)} / {BudgetMb} MB";
}
