using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GC.Services;

/// <summary>
/// 找到并启动 GC-SAVEDOG（启动器更新的守护进程）。
///
/// 为什么要独立进程：启动器正在运行时无法安全覆盖自己；一旦覆盖失败，
/// 启动器已退出，就没人能回滚。把"替换 + 看护"交给一个只做这件事的小程序，
/// 风险面被压缩到最小，也便于审计。
/// </summary>
public static class SavedogLauncher
{
    /// <summary>在安装目录里寻找 SAVEDOG 可执行文件。</summary>
    public static string? Find(AppPaths paths)
    {
        var name = SavedogFiles.ExecutableName;
        var install = paths.InstallDirectory;

        // 首选 .GC/safedog：更新时整目录替换不会碰这里，正在运行的守护进程也安全。
        var home = Path.Combine(paths.SafedogRoot, name);
        if (File.Exists(home)) return home;

        // 包内 payload：非本体文件统一收在 .GC/bin
        var payload = Path.Combine(install, ".GC", "bin", name);
        if (File.Exists(payload)) return payload;

        var direct = Path.Combine(install, name);
        if (File.Exists(direct)) return direct;

        // 1.0.10 及更早的包把守护进程放在 safedog/ 子目录
        var packaged = Path.Combine(install, "safedog", name);
        if (File.Exists(packaged)) return packaged;

        // macOS：可能放在 .app 内部
        try
        {
            foreach (var app in Directory.GetDirectories(install, "*.app"))
            {
                var inside = Path.Combine(app, "Contents", "MacOS", name);
                if (File.Exists(inside)) return inside;
            }
        }
        catch { }

        var bin = Path.Combine(paths.HomeRoot, "bin", name);
        if (File.Exists(bin)) return bin;

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var beside = Path.Combine(Path.GetDirectoryName(exe)!, name);
                if (File.Exists(beside)) return beside;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 启动 SAVEDOG 应用更新。<paramref name="waitForPid"/> 是当前启动器进程，
    /// SAVEDOG 会等它退出后再动文件。
    /// </summary>
    public static bool TryStart(AppPaths paths, int waitForPid, out string? error)
    {
        error = null;
        var exe = Find(paths);
        if (exe == null)
        {
            error = "未找到 GC-SAVEDOG";
            return false;
        }

        try
        {
            var requestPath = Path.Combine(paths.UpdateRoot, SavedogFiles.ApplyRequest);
            if (!File.Exists(requestPath))
            {
                error = "缺少 apply-request.json";
                return false;
            }

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = paths.InstallDirectory
            };
            psi.ArgumentList.Add("apply");
            psi.ArgumentList.Add("--update");
            psi.ArgumentList.Add(paths.UpdateRoot);
            psi.ArgumentList.Add("--install");
            psi.ArgumentList.Add(paths.InstallDirectory);
            psi.ArgumentList.Add("--wait-pid");
            psi.ArgumentList.Add(waitForPid.ToString());
            psi.ArgumentList.Add("--timeout");
            psi.ArgumentList.Add("45");

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var mode = File.GetUnixFileMode(exe);
                    File.SetUnixFileMode(exe, mode | UnixFileMode.UserExecute);
                }
                catch { }
            }

            Process.Start(psi);
            Console.WriteLine($"[update] GC-SAVEDOG 已启动: {exe}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>读取 SAVEDOG 最近一次的执行结果（供界面展示）。</summary>
    public static SavedogState? ReadState(AppPaths paths)
        => Json.Read<SavedogState>(Path.Combine(paths.UpdateRoot, SavedogFiles.State));

    /// <summary>上次成功启动的版本（回滚依据）。</summary>
    public static LastGoodVersion? ReadLastGood(AppPaths paths)
        => Json.Read<LastGoodVersion>(Path.Combine(paths.UpdateRoot, SavedogFiles.LastGood));
}
