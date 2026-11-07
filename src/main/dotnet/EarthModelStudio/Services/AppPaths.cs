using System;
using System.IO;

namespace EarthModelStudio.Services;

/// <summary>
/// 运行目录约定（可执行文件旁边，不内置任何模型）：
/// <code>
/// &lt;安装目录&gt;/
///   EarthModelStudio(.exe | .app/...)
///   .GC/
///     settings.json     设置与已安装版本记录
///     model/            模型（启动时下载；model 目录也在 .GC 里）
///     cache/            下载缓存 / 原子替换暂存
///     update/           待下次启动应用的启动器更新包
/// </code>
/// 开发态同样使用 .GC/model，并自动把仓库 model/ 的可分发部分同步进去。
/// EMS_MODELS / EMS_DATA / EMS_HOME 可显式覆盖。
/// </summary>
public sealed class AppPaths
{
    /// <summary>安装目录（开发态为仓库根）。</summary>
    public string AppRoot { get; private init; } = "";

    /// <summary>可执行文件真正所在目录：启动器自更新时要覆盖这里。</summary>
    public string InstallDirectory { get; private init; } = "";

    /// <summary>.GC 目录。</summary>
    public string HomeRoot { get; private init; } = "";

    /// <summary>模型库，恒为 &lt;HomeRoot&gt;/model。</summary>
    public string ModelRoot { get; private init; } = "";

    /// <summary>兼容旧代码：数据目录即 .GC。</summary>
    public string DataRoot => HomeRoot;

    public string CacheRoot => Path.Combine(HomeRoot, "cache");
    public string UpdateRoot => Path.Combine(HomeRoot, "update");
    public string SettingsFile => Path.Combine(HomeRoot, "settings.json");

    public bool IsDevelopment { get; private init; }
    public bool IsPortableModelRoot { get; private init; }

    public static AppPaths Discover()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        var repoRoot = Environment.GetEnvironmentVariable("EMS_HOME");
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            repoRoot = FindRepoRoot(baseDir);
        var isDev = !string.IsNullOrEmpty(repoRoot) &&
                    File.Exists(Path.Combine(repoRoot!, "EarthModelStudio.sln"));
        var appRoot = isDev ? repoRoot! : baseDir;

        // 只读安装位置（如 Program Files / /Applications）自动退回用户数据目录
        var explicitData = Environment.GetEnvironmentVariable("EMS_DATA");
        string home;
        if (!string.IsNullOrWhiteSpace(explicitData))
        {
            home = explicitData!;
        }
        else
        {
            var portableHome = Path.Combine(appRoot, ".GC");
            home = CanWrite(portableHome)
                ? portableHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EarthModelStudio", ".GC");
        }

        var envModels = Environment.GetEnvironmentVariable("EMS_MODELS");
        var modelRoot = !string.IsNullOrWhiteSpace(envModels)
            ? envModels!
            : Path.Combine(home, "model");      // 统一：model 目录在 .GC 里

        return new AppPaths
        {
            AppRoot = appRoot,
            InstallDirectory = baseDir,
            HomeRoot = home,
            ModelRoot = modelRoot,
            IsDevelopment = isDev,
            IsPortableModelRoot = string.IsNullOrWhiteSpace(envModels)
        };
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
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
        Directory.CreateDirectory(HomeRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(ModelRoot);
        Directory.CreateDirectory(UpdateRoot);

        // 开发态：让 .GC/model 与仓库里的模型保持一致（只同步清单 + 数据，不含 src 源码）
        if (IsDevelopment && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EMS_MODELS")))
            SyncDevelopmentModels();
    }

    private void SyncDevelopmentModels()
    {
        try
        {
            var source = Path.Combine(AppRoot, "model");
            if (!Directory.Exists(source)) return;
            foreach (var dir in Directory.GetDirectories(source))
            {
                var id = Path.GetFileName(dir);
                if (id.StartsWith('_')) continue;
                var manifest = Path.Combine(dir, id + ".json");
                var data = Path.Combine(dir, id + ".model");
                if (!File.Exists(manifest) || !Directory.Exists(data)) continue;
                var dest = Path.Combine(ModelRoot, id);
                Directory.CreateDirectory(dest);
                File.Copy(manifest, Path.Combine(dest, id + ".json"), true);
                CopyTree(data, Path.Combine(dest, id + ".model"));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"同步开发模型失败: {ex.Message}");
        }
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith('.')) continue;
            var to = Path.Combine(dst, Path.GetRelativePath(src, file));
            var parent = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.Copy(file, to, true);
        }
    }
}
