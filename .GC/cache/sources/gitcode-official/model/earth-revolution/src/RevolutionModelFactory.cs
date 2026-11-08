using EarthModelStudio.ModelSdk;

namespace NeoRF.EarthModels;

/// <summary>地球公转模型入口（清单里的 type 指向这里）。</summary>
public sealed class RevolutionModelFactory : IModelFactory
{
    public int ApiVersion => EarthModelRuntime.SupportedApiVersion;

    public IModelRuntime Create() => new EarthModelRuntime();
}
