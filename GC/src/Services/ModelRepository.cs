using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GC.Models;

namespace GC.Services;

/// <summary>
/// 本地模型库。启动器只**扫描 / 删除**模型包，不再解析或缓存任何场景数据 ——
/// 场景、贴图、界面都由模型自己的程序集负责。
///
/// 扫描结果与目录体积都做短时缓存：前端（首页 / 下载页）会频繁轮询
/// <c>/api/models</c>，而每个模型的体积要递归遍历整棵目录。没有缓存时
/// 一次刷新就是成千上万次 stat，低配机器上会明显卡顿。
/// </summary>
public sealed class ModelRepository
{
    /// <summary>扫描结果缓存时长：够挡住连点刷新，又不会让手动放进来的模型等太久。</summary>
    private static readonly TimeSpan ScanTtl = TimeSpan.FromSeconds(2);

    /// <summary>目录体积缓存时长（目录 mtime 变了会提前失效）。</summary>
    private static readonly TimeSpan SizeTtl = TimeSpan.FromMinutes(1);

    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Size, DateTime Stamp, DateTime At)> _sizes =
        new(StringComparer.OrdinalIgnoreCase);

    private List<ModelPackage>? _cache;
    private DateTime _cacheAt = DateTime.MinValue;

    public ModelRepository(AppPaths paths) => _paths = paths;

    public event Action? Changed;

    public IReadOnlyList<ModelPackage> Scan()
    {
        lock (_gate)
        {
            if (_cache != null && DateTime.UtcNow - _cacheAt < ScanTtl) return _cache;
        }

        _paths.WaitForDevelopmentModels();

        var result = new List<ModelPackage>();
        foreach (var (manifest, dir) in ModelScanner.Scan(_paths.ModelRoot))
        {
            result.Add(new ModelPackage
            {
                Manifest = manifest,
                Directory = dir,
                SizeBytes = SizeOf(dir)
            });
        }

        lock (_gate)
        {
            _cache = result;
            _cacheAt = DateTime.UtcNow;
        }
        return result;
    }

    public ModelPackage? Find(string id)
        => Scan().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool Delete(string id)
    {
        var package = Find(id);
        if (package == null) return false;
        try
        {
            Directory.Delete(package.Directory, true);
            Invalidate();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"删除模型失败: {ex.Message}");
            return false;
        }
    }

    public void NotifyChanged()
    {
        Invalidate();
        Changed?.Invoke();
    }

    /// <summary>清空缓存：模型被安装 / 删除 / 覆盖后必须调用。</summary>
    private void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
            _cacheAt = DateTime.MinValue;
        }
        lock (_sizes) _sizes.Clear();
    }

    /// <summary>目录体积，按「目录 mtime + TTL」缓存。</summary>
    private long SizeOf(string dir)
    {
        DateTime stamp;
        try { stamp = Directory.GetLastWriteTimeUtc(dir); }
        catch { stamp = DateTime.MinValue; }

        lock (_sizes)
        {
            if (_sizes.TryGetValue(dir, out var cached) &&
                cached.Stamp == stamp &&
                DateTime.UtcNow - cached.At < SizeTtl)
                return cached.Size;
        }

        var size = ModelScanner.DirectorySize(dir);
        lock (_sizes) _sizes[dir] = (size, stamp, DateTime.UtcNow);
        return size;
    }
}
