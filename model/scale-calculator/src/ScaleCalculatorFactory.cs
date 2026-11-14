using GC.ModelSdk;

namespace NeoRF.ScaleCalculator;

/// <summary>比例尺计算器模型入口（清单里的 type 指向这里）。</summary>
public sealed class ScaleCalculatorFactory : IModelFactory
{
    public int ApiVersion => 1;
    public IModelRuntime Create() => new ScaleCalculatorRuntime();
}
