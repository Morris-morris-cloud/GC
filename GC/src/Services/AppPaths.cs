using System;
using System.Threading.Tasks;
using System.IO;

namespace GC.Services;

/// <summary>
/// 运行目录约定（可执行文件旁边，不内置任何模型）：
/// <code>
/// &lt;安装目录&gt;/
///   GC(.exe | .app/...)
///   .GC/
///     settings.json     设置与已安装版本记录
///     model/            模型（启动时下载）
///     cache/            下载缓存 / 原子替换暂存
///     update/           待下次启动应用的启动器更新包
/// </code>
/// 开发态同样使用仓库根 .GC/model，并自动同步仓库 model/ 的可分发部分。
/// EMS_MODELS / EMS_DATA / EMS_HOME 可显式覆盖。
/// </summary>
public sealed class AppPaths
{
    private Task _developmentModelsSync = Task.CompletedTask;

    /// <summary>安装目录（开发态为仓库根）。</summary>
    public string AppRoot { get; private init; } = "";

    /// <summary>可执行文件真正所在目录：启动器自更新时要覆盖这里。</summary>
    public string InstallDirectory { get; private init; } = "";

    /// <summary>.GC 目录。</summary>
    public string HomeRoot { get; private init; } = "";

    /// <summary>模型库。</summary>
    public string ModelRoot { get; private init; } = "";

    /// <summary>兼容旧代码：数据目录即 .GC。</summary>
    public string DataRoot => HomeRoot;

    public string CacheRoot => Path.Combine(HomeRoot, "cache");
    public string UpdateRoot => Path.Combine(HomeRoot, "update");

    /// <summary>用户配置目录（与可重建的缓存分开，删 cache 不会丢设置）。</summary>
    public string ConfigRoot => Path.Combine(HomeRoot, "config");

    public string SettingsFile => Path.Combine(ConfigRoot, "settings.json");

    public bool IsDevelopment { get; private init; }
    public bool IsPortableModelRoot { get; private init; }

    public static AppPaths Discover()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // 判定「当前目录」：程序实际所在的位置。
        //   · 发布版：可执行文件所在目录（单文件时为 exe 旁，否则为 apphost 目录）
        //   · 源码版：仓库根（含 GC.sln 的目录）
        //
        // 重要：源码版由 `dotnet run` 启动时 Environment.ProcessPath 指向 dotnet 宿主，
        // 直接用它会把 .GC 与更新包写到 dotnet 安装目录去，所以必须先识别源码版。
        var explicitHome = Environment.GetEnvironmentVariable("EMS_HOME");
        var repoRoot = explicitHome;
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            repoRoot = FindRepoRoot(baseDir);
        var isDev = !string.IsNullOrEmpty(repoRoot) &&
                    File.Exists(Path.Combine(repoRoot!, "GC.sln"));

        string installDir;
        if (isDev)
        {
            // 源码版：安装目录 = 仓库根
            installDir = repoRoot!;
        }
        else if (!string.IsNullOrWhiteSpace(explicitHome) && Directory.Exists(explicitHome))
        {
            installDir = explicitHome;
        }
        else
        {
            // 发布版：运行目录 = **放着 .app / .exe 的那一层**，不是 .app 内部的 Contents/MacOS。
            //
            //   <运行目录>/                     ← 要的就是这一层
            //     GC-1.0.3-mac-arm64.app/        ← 程序包
            //     .GC/                           ← 用户数据放这里
            //
            // 若直接返回 Contents/MacOS，.GC 会被塞进 app 包内部，
            // 用户看不到，重装/替换时也会跟着包一起被删。
            installDir = baseDir;
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    var exeName = Path.GetFileNameWithoutExtension(exe);
                    var isDotnetHost = string.Equals(exeName, "dotnet", StringComparison.OrdinalIgnoreCase);
                    var exeDir = Path.GetDirectoryName(exe);

                    if (!isDotnetHost && !string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
                    {
                        // 从可执行文件所在目录向上找：如果处在 Xxx.app/Contents/MacOS，
                        // 则运行目录是 Xxx.app 的**父目录**。
                        installDir = ResolveRunDirectory(exeDir!);
                    }
                }
            }
            catch { /* 取不到就沿用 BaseDirectory */ }
        }

        // 运行目录 = 程序所在目录。.GC 永远紧挨着程序，不往系统目录放。
        var appRoot = installDir;

        // 只读安装位置（如 Program Files / /Applications）才回退到用户数据目录；
        // 正常可写时一律用当前目录下的 .GC。
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
                    "GC", ".GC");
        }

        var envModels = Environment.GetEnvironmentVariable("EMS_MODELS");
        var modelRoot = !string.IsNullOrWhiteSpace(envModels)
            ? envModels!
            : Path.Combine(home, "model");

        return new AppPaths
        {
            AppRoot = appRoot,
            InstallDirectory = installDir,
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
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从程序所在位置向上找仓库根（含 GC.sln 的目录）。
    ///
    /// 关键：程序若位于 .app 包内（或 dist/ 等发布产物目录），**不算开发态**。
    /// 否则把 .app 放在仓库的 dist/ 下运行时，会一路向上命中仓库根的 .sln，
    /// 被误判为开发态，导致 .GC 跑到仓库根而不是程序旁边。
    /// </summary>
    private static string? FindRepoRoot(string start)
    {
        // 在 .app 包内（.../Xxx.app/Contents/MacOS）直接视为发布版
        if (IsInsideAppBundle(start)) return null;

        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            // 碰到发布产物目录也停下：dist/ 输出、或带 .GC 的运行目录都不是源码树
            var name = dir.Name;
            if (string.Equals(name, "dist", StringComparison.OrdinalIgnoreCase)) return null;

            if (File.Exists(Path.Combine(dir.FullName, "GC.sln")))
            {
                // 再确认不是从「发布输出目录」里往上找来的
                if (IsUnderPublishOutput(start, dir.FullName)) return null;
                return dir.FullName;
            }
        }
        return null;
    }

    /// <summary>
    /// 求「运行目录」：程序包所在的父目录。
    ///   · 若路径处在 Xxx.app/Contents/MacOS 里，返回 Xxx.app 的父目录
    ///   · 其它情况（Windows exe 目录）原样返回
    /// </summary>
    private static string ResolveRunDirectory(string exeDir)
    {
        var dir = new DirectoryInfo(exeDir);

        // 走到 app 包根：.../Xxx.app
        string? appRoot = null;
        for (var cur = dir; cur != null; cur = cur.Parent)
        {
            if (cur.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                appRoot = cur.FullName;
                break;
            }
            if (cur.Parent == null) break;
        }

        if (appRoot != null)
        {
            // 返回 .app 的父目录（即用户双击时看到的那一层）
            var parent = Path.GetDirectoryName(appRoot);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                return parent!;
            return appRoot;
        }

        return exeDir;
    }

    /// <summary>路径是否位于某个 .app 包内部。</summary>
    private static bool IsInsideAppBundle(string path)
    {
        var p = path.Replace('\\', '/');
        var idx = p.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        return idx >= 0;
    }

    /// <summary>
    /// 从 start 到 repoRoot 之间是否经过了发布输出目录（dist / bin / publish）。
    /// 经过则说明程序是打包产物，而非源码树里直接运行的进程。
    /// </summary>
    private static bool IsUnderPublishOutput(string start, string repoRoot)
    {
        try
        {
            var rel = Path.GetRelativePath(repoRoot, start).Replace('\\', '/');
            foreach (var seg in rel.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(seg, "dist", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(seg, "publish", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(seg, "Release", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(HomeRoot);
        Directory.CreateDirectory(ConfigRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(ModelRoot);
        Directory.CreateDirectory(UpdateRoot);

        MigrateLegacySettings();

        // 开发态：让 .GC/model 与仓库里的模型保持一致（只同步清单 + 数据，不含 src 源码）。
        // 放到后台线程：这一步要复制 coastline.json 等大文件，同步做会拖慢启动。
        if (IsDevelopment && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EMS_MODELS")))
        {
            var modelRoot = ModelRoot;
            var source = Path.Combine(AppRoot, "model");
            if (Directory.Exists(source))
                _developmentModelsSync = Task.Run(() => SyncDevelopmentModels(modelRoot, source));
        }
    }

    public void WaitForDevelopmentModels() => _developmentModelsSync.GetAwaiter().GetResult();

    /// <summary>
    /// 老版本把设置放在 .GC/settings.json。现在设置归到 .GC/config/，
    /// 与 cache/ 分开（清缓存不会连带清掉语言、内存预算、离线源等配置）。
    /// 迁移只做一次：新位置不存在时才搬旧的。
    /// </summary>
    private void MigrateLegacySettings()
    {
        try
        {
            var legacy = Path.Combine(HomeRoot, "settings.json");
            if (!File.Exists(legacy) || File.Exists(SettingsFile)) return;
            Directory.CreateDirectory(ConfigRoot);
            File.Move(legacy, SettingsFile);
            Console.WriteLine("[paths] 设置已迁移到 .GC/config/settings.json");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"迁移旧设置失败: {ex.Message}");
        }
    }

    private static void SyncDevelopmentModels(string modelRoot, string source)
    {
        try
        {
            if (!Directory.Exists(source)) return;
            foreach (var dir in Directory.GetDirectories(source))
            {
                var id = Path.GetFileName(dir);
                if (id.StartsWith('_')) continue;
                var manifest = Path.Combine(dir, id + ".json");
                var data = Path.Combine(dir, id + ".model");
                if (!File.Exists(manifest) || !Directory.Exists(data)) continue;
                var dest = Path.Combine(modelRoot, id);
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
