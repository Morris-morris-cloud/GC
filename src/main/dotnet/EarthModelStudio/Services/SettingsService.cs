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
    /// 默认下载源（gitcode 官方仓库）始终内置在列表里，且不允许删除：
    /// 它是启动器自动更新的同源仓库，用户清掉会导致模型无法回退。
    /// 老配置里没有这条时自动补上；已有的用户自定义源保持不变。
    /// </summary>
    public const string DefaultSourceId = "gitcode-official";
    public const string DefaultSourceUrl = "https://gitcode.com/2509_92447106/GC";

    private void SeedDefaultSource()
    {
        var existing = Current.Sources.FirstOrDefault(s =>
            string.Equals(s.Id, DefaultSourceId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            Current.Sources.Insert(0, new ModelSource
            {
                Id = DefaultSourceId,
                Name = "Morris GC (GitCode)",
                Url = DefaultSourceUrl,
                Enabled = true,
                BuiltIn = true
            });
            Save();
        }
        else
        {
            // 强制保证内置标记与地址，避免旧配置被用户改坏后失去保护
            existing.BuiltIn = true;
            if (string.IsNullOrWhiteSpace(existing.Url)) existing.Url = DefaultSourceUrl;
        }
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
