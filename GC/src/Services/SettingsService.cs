using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GC.Models;

namespace GC.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;

    public AppSettings Current { get; private set; } = new();
    public event Action? Changed;

    public SettingsService(AppPaths paths) => _paths = paths;

    public void Load()
    {
        Current = Json.Read<AppSettings>(_paths.SettingsFile) ?? new AppSettings();

        // 暂时停用 GitCode：旧配置里的 GitCode 仓库地址统一迁到 Xhmcl。
        if (Current.UpdateRepository.Contains("gitcode.com", StringComparison.OrdinalIgnoreCase))
        {
            Current.UpdateRepository = SourcePreset.Xhmcl.HomeUrl;
            Save();
        }
        SeedDefaultSource();
        if (Current.MemoryMb < 256) Current.MemoryMb = 2048;
    }

    /// <summary>
    /// 当前只启用 Xhmcl 一个内置源。GitCode 的定义与下载实现保留在代码里，
    /// 但不再参与测速 / 下载 / 更新；恢复时把 GitCode 加回 <see cref="SourcePreset.All"/> 即可。
    /// </summary>
    public const string DefaultSourceId = "xhmcl";
    public const string DefaultSourceUrl = "https://git.xhmcl.com/Morris/GC";

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
