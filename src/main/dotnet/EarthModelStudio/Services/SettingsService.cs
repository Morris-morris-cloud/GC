using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EarthModelStudio.Models;

namespace EarthModelStudio.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;

    public AppSettings Current { get; private set; } = new();
    public event Action? Changed;

    public SettingsService(AppPaths paths) => _paths = paths;

    public void Load()
    {
        Current = Json.Read<AppSettings>(_paths.SettingsFile) ?? new AppSettings();
        SeedDefaultSource();
        if (Current.MemoryMb < 256) Current.MemoryMb = 2048;
    }

    /// <summary>
    /// 只允许两个内置源：Xhmcl 与 GitCode（同一份仓库的两个镜像）。
    /// 不再支持用户自定义源 —— 每次加载都把 Sources 规整成这两条，
    /// 老配置里的自定义源会被清理掉。
    /// </summary>
    public const string DefaultSourceId = "gitcode";
    public const string DefaultSourceUrl = "https://gitcode.com/2509_92447106/GC";

    private void SeedDefaultSource()
    {
        var rebuilt = new List<ModelSource>();
        foreach (var preset in SourcePreset.All)
        {
            // 保留用户对「启用/停用」的选择，其余字段以内置定义为准
            var old = Current.Sources.FirstOrDefault(s =>
                SourcePreset.Detect(s.Url).Id == preset.Id);
            rebuilt.Add(new ModelSource
            {
                Id = preset.Id,
                Name = preset.Name,
                Url = preset.HomeUrl,
                Enabled = old?.Enabled ?? true,
                BuiltIn = true
            });
        }

        var changed = Current.Sources.Count != rebuilt.Count ||
                      Current.Sources.Any(s => !s.BuiltIn);
        Current.Sources = rebuilt;
        if (changed) Save();
    }

    public void Save()
    {
        try
        {
            Json.Write(_paths.SettingsFile, Current);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"保存设置失败: {ex.Message}");
        }
    }

    public void Apply(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
        Changed?.Invoke();
    }

    public void Notify()
    {
        Save();
        Changed?.Invoke();
    }
}
