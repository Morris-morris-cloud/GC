using System;
using System.IO;

namespace EarthModelStudio.Services;

/// <summary>
/// 目录探测。启动器本身不含任何模型数据，模型一律从外部模型库目录读取：
/// 1) 开发态：从 bin 目录向上找到 EarthModelStudio.sln，模型库用仓库根 model/
/// 2) 绿色态：可执行文件旁边有 model/ 目录（发布包默认只建空目录）
/// 3) 安装态：模型库放到用户数据目录
/// 也可用环境变量 EMS_HOME / EMS_MODELS 显式指定。
/// </summary>
public sealed class AppPaths
{
    public string AppRoot { get; private init; } = "";
    public string ModelRoot { get; private init; } = "";
    public string DataRoot { get; private init; } = "";
    public bool IsPortableModelRoot { get; private init; }

    public string CacheRoot => Path.Combine(DataRoot, "cache");
    public string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public static AppPaths Discover()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var dataRoot = Environment.GetEnvironmentVariable("EMS_DATA")
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                       "EarthModelStudio");

        // 显式指定模型库（模型是外置数据，可放在任意磁盘/网络盘）
        var envModels = Environment.GetEnvironmentVariable("EMS_MODELS");
        if (!string.IsNullOrWhiteSpace(envModels))
        {
            return new AppPaths
            {
                AppRoot = baseDir,
                ModelRoot = envModels!,
                DataRoot = dataRoot,
                IsPortableModelRoot = false
            };
        }

        var repoRoot = Environment.GetEnvironmentVariable("EMS_HOME");
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            repoRoot = FindRepoRoot(baseDir);

        var exeModelDir = Path.Combine(baseDir, "model");
        string appRoot, modelRoot;
        bool portable = false;

        if (!string.IsNullOrEmpty(repoRoot) && Directory.Exists(Path.Combine(repoRoot!, "model")))
        {
            appRoot = repoRoot!;
            modelRoot = Path.Combine(appRoot, "model");
        }
        else if (Directory.Exists(exeModelDir))
        {
            appRoot = baseDir;
            modelRoot = exeModelDir;
            portable = true;
        }
        else
        {
            appRoot = baseDir;
            modelRoot = Path.Combine(dataRoot, "model");
        }

        return new AppPaths
        {
            AppRoot = appRoot,
            ModelRoot = modelRoot,
            DataRoot = dataRoot,
            IsPortableModelRoot = portable
        };
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EarthModelStudio.sln"))) return dir.FullName;
        }
        return null;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(ModelRoot);
    }
}
