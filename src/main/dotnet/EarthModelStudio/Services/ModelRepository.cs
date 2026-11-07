using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EarthModelStudio.Models;

namespace EarthModelStudio.Services;

/// <summary>
/// 本地模型库。启动器只**扫描 / 删除**模型包，不再解析或缓存任何场景数据 ——
/// 场景、贴图、界面都由模型自己的程序集负责。
/// </summary>
public sealed class ModelRepository
{
    private readonly AppPaths _paths;

    public ModelRepository(AppPaths paths) => _paths = paths;

    public event Action? Changed;

    public IReadOnlyList<ModelPackage> Scan()
    {
        var result = new List<ModelPackage>();
        foreach (var (manifest, dir) in ModelScanner.Scan(_paths.ModelRoot))
        {
            result.Add(new ModelPackage
            {
                Manifest = manifest,
                Directory = dir,
                SizeBytes = ModelScanner.DirectorySize(dir)
            });
        }
        return result;
    }

    public ModelPackage? Find(string id) => Scan().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool Delete(string id)
    {
        var package = Find(id);
        if (package == null) return false;
        try
        {
            Directory.Delete(package.Directory, true);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"删除模型失败: {ex.Message}");
            return false;
        }
    }

    public void NotifyChanged() => Changed?.Invoke();
}
