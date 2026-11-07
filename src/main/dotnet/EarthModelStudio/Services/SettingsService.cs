using System;
using System.Collections.Generic;
using System.IO;
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
        if (Current.Sources.Count == 0) SeedSources();
        if (Current.MemoryMb < 256) Current.MemoryMb = 2048;
    }

    /// <summary>
    /// 自动更新由 UpdateService 负责（版本清单 version.json），这里不预置任何在线源；
    /// 只给「下载」页留一条空的自定义源占位。
    /// </summary>
    private void SeedSources()
    {
        Current.Sources.Add(new ModelSource
        {
            Id = "custom",
            Name = "自定义源",
            Url = "",
            Enabled = false
        });
        Save();
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
