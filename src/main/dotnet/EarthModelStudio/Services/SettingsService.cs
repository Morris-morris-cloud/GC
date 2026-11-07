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
    /// 首次启动不预置任何模型源：启动器只做适配，模型内容由用户从链接导入。
    /// 这里只给一条空的自定义源占位。
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
