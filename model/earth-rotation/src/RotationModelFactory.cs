using GC.ModelSdk;

namespace NeoRF.EarthModels;

/// <summary>地球自转模型入口（清单里的 type 指向这里）。</summary>
public sealed class RotationModelFactory : IModelFactory
{
    public int ApiVersion => EarthModelRuntime.SupportedApiVersion;

    public IModelRuntime Create() => new EarthModelRuntime();
}
