using System;
using System.IO;

namespace GC.Services;

/// <summary>
/// 运行期目录准备：
/// <list type="number">
///   <item>创建 <c>.GC/logs</c> 与 <c>.GC/safedog</c>；</item>
///   <item>把安装包里的守护进程复制到 <c>.GC/safedog</c>（程序本体与用户数据分开）；</item>
///   <item>初始化双语日志并写一条启动记录。</item>
/// </list>
///
/// 为什么要复制而不是直接用包里的：更新时会整目录替换程序文件，
/// 正在运行的守护进程无法被覆盖。放到 .GC 后，整目录替换永远不碰它，
/// 新版守护进程在下一次启动时复制进来即可。
/// </summary>
public static class RuntimeLayout
{
    /// <summary>准备目录与日志，返回实际使用的守护进程路径（可能为 null）。</summary>
    public static string? Ensure(AppPaths paths)
    {
        try { Directory.CreateDirectory(paths.LogsRoot); } catch { }
        try { Directory.CreateDirectory(paths.SafedogRoot); } catch { }

        GcLog.Init(paths.LogsRoot);

        var safedog = InstallSafedog(paths);

        GcLog.Info(
            $"启动 GC {LauncherRuntimeInfo.CurrentLauncherVersion}（{LauncherRuntimeInfo.CurrentRid}），" +
            $"数据目录 {paths.HomeRoot}，守护进程 {(safedog == null ? "未找到" : Path.GetFileName(safedog))}",
            $"Starting GC {LauncherRuntimeInfo.CurrentLauncherVersion} ({LauncherRuntimeInfo.CurrentRid}), " +
            $"data directory {paths.HomeRoot}, safedog {(safedog == null ? "not found" : Path.GetFileName(safedog))}");

        return safedog;
    }

    /// <summary>把包内守护进程同步到 <c>.GC/safedog</c>。已是最新则不动。</summary>
    private static string? InstallSafedog(AppPaths paths)
    {
        var name = SavedogFiles.ExecutableName;
        var target = Path.Combine(paths.SafedogRoot, name);

        var source = FindPackaged(paths, name);
        if (source != null && NeedsCopy(source, target))
        {
            try
            {
                Directory.CreateDirectory(paths.SafedogRoot);
                File.Copy(source, target, overwrite: true);
                MarkExecutable(target);
                GcLog.Info($"已更新守护进程到 {target}", $"Safedog updated at {target}");
            }
            catch (Exception ex)
            {
                GcLog.Warn($"复制守护进程失败（继续使用已有版本）：{ex.Message}",
                    $"Failed to copy safedog (falling back to the existing one): {ex.Message}");
            }
        }

        return File.Exists(target) ? target : source;
    }

    private static string? FindPackaged(AppPaths paths, string name)
    {
        var candidates = new[]
        {
            Path.Combine(paths.InstallDirectory, "safedog", name),
            Path.Combine(paths.InstallDirectory, name),
            Path.Combine(paths.AppRoot, "safedog", name),
            Path.Combine(paths.AppRoot, name),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        // macOS：可能在 .app/Contents/MacOS 里
        try
        {
            foreach (var app in Directory.GetDirectories(paths.InstallDirectory, "*.app"))
            {
                var inside = Path.Combine(app, "Contents", "MacOS", name);
                if (File.Exists(inside)) return inside;
            }
        }
        catch { }

        return null;
    }

    private static bool NeedsCopy(string source, string target)
    {
        try
        {
            if (!File.Exists(target)) return true;
            var a = new FileInfo(source);
            var b = new FileInfo(target);
            return a.Length != b.Length || a.LastWriteTimeUtc > b.LastWriteTimeUtc;
        }
        catch
        {
            return true;
        }
    }

    private static void MarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute |
                                       UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch { }
    }
}
