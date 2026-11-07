using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using EarthModelStudio.Models;

namespace EarthModelStudio.Services;

/// <summary>.GC/update/pending.json：已下载、等待下次启动应用的启动器版本。</summary>
public sealed class PendingUpdate
{
    public string Version { get; set; } = "";
    public string Rid { get; set; } = "";
    public string InstallDirectory { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Payload { get; set; } = "payload";
    public string? DownloadedAt { get; set; }
}

/// <summary>
/// 「下次启动时应用」：主进程刚进 Main、还没建窗口时，交给一个独立脚本进程，
/// 脚本等主进程退出后覆盖可执行文件并重启。这样 Windows 上也不会碰到文件占用。
/// </summary>
public static class LauncherUpdate
{
    public static bool TryApplyPending(AppPaths paths) => TryApplyPending(paths, launch: true);

    /// <summary>launch=false 只生成脚本、不启动进程，供离线自动更新测试使用。</summary>
    public static bool TryApplyPending(AppPaths paths, bool launch)
    {
        // 开发态、自检、显式关闭时一律不替换正在运行的程序
        if (paths.IsDevelopment) return false;
        if (Environment.GetEnvironmentVariable("EMS_SELFTEST") == "1") return false;
        if (Environment.GetEnvironmentVariable("EMS_NO_SELF_UPDATE") == "1") return false;

        var pendingFile = Path.Combine(paths.UpdateRoot, "pending.json");
        if (!File.Exists(pendingFile))
        {
            ClearPendingSetting(paths);
            return false;
        }

        PendingUpdate? pending;
        try { pending = Json.Read<PendingUpdate>(pendingFile); }
        catch { pending = null; }

        var payload = Path.Combine(paths.UpdateRoot,
            string.IsNullOrWhiteSpace(pending?.Payload) ? "payload" : pending!.Payload!);
        var installDir = string.IsNullOrWhiteSpace(pending?.InstallDirectory)
            ? paths.InstallDirectory : pending!.InstallDirectory!;

        // 版本已经生效，或包不完整：清掉标记后正常启动
        if (pending == null || string.IsNullOrWhiteSpace(pending.Version) ||
            string.Equals(pending.Version, UpdateService.CurrentLauncherVersion,
                StringComparison.OrdinalIgnoreCase) || !Directory.Exists(payload))
        {
            TryDelete(pendingFile);
            ClearPendingSetting(paths);
            return false;
        }

        var exeName = string.IsNullOrWhiteSpace(pending.Executable)
            ? (OperatingSystem.IsWindows() ? "EarthModelStudio.exe" : "EarthModelStudio")
            : pending.Executable!;
        var exePath = Path.Combine(installDir, exeName);

        var isWindows = OperatingSystem.IsWindows();
        var script = Path.Combine(paths.UpdateRoot, isWindows ? "apply-update.bat" : "apply-update.sh");
        var content = isWindows
            ? WindowsScript(payload, installDir, exePath, pendingFile, script)
            : UnixScript(payload, installDir, exePath, pendingFile, script);

        try
        {
            File.WriteAllText(script, content, new UTF8Encoding(false));
            if (!launch)
            {
                Console.WriteLine($"(dry-run) 更新脚本已生成: {script}");
                return true;
            }

            ProcessStartInfo psi;
            if (isWindows)
            {
                psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(script);
            }
            else
            {
                try
                {
                    File.SetUnixFileMode(script,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch { /* 某些文件系统不支持 */ }
                psi = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(script);
            }

            Process.Start(psi);
            Console.WriteLine($"启动器更新 {pending.Version} 已安排，重启后生效");
            return true; // Main 直接返回，进程退出后由脚本接管
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"应用启动器更新失败（保留待更新包，下次再试）: {ex.Message}");
            return false;
        }
    }

    private static string WindowsScript(string payload, string installDir,
        string exePath, string pendingFile, string script)
    {
        var text = new StringBuilder();
        text.AppendLine("@echo off");
        text.AppendLine("chcp 65001 >nul 2>&1");
        text.AppendLine($"set \"SRC={payload}\"");
        text.AppendLine($"set \"DST={installDir}\"");
        text.AppendLine($"set \"EXE={exePath}\"");
        text.AppendLine($"set \"PENDING={pendingFile}\"");
        text.AppendLine("timeout /t 2 /nobreak >nul");
        text.AppendLine("xcopy /E /Y /I /Q \"%SRC%\\*\" \"%DST%\\\" >nul 2>&1");
        text.AppendLine("if exist \"%PENDING%\" del /Q \"%PENDING%\" >nul 2>&1");
        text.AppendLine("if exist \"%EXE%\" start \"\" \"%EXE%\"");
        text.AppendLine($"del /Q \"{script}\" >nul 2>&1");
        return text.ToString();
    }

    private static string UnixScript(string payload, string installDir,
        string exePath, string pendingFile, string script)
    {
        static string Q(string value) => "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`") + "\"";

        var text = new StringBuilder();
        text.AppendLine("#!/bin/sh");
        text.AppendLine("sleep 2");
        text.AppendLine($"cp -R {Q(payload + "/.")} {Q(installDir + "/")}");
        text.AppendLine($"chmod +x {Q(exePath)} 2>/dev/null");
        text.AppendLine($"rm -f {Q(pendingFile)}");
        text.AppendLine($"{Q(exePath)} >/dev/null 2>&1 &");
        text.AppendLine($"rm -f {Q(script)}");
        return text.ToString();
    }

    private static void ClearPendingSetting(AppPaths paths)
    {
        try
        {
            var settings = Json.Read<AppSettings>(paths.SettingsFile);
            if (settings == null || string.IsNullOrEmpty(settings.PendingLauncherVersion)) return;
            settings.PendingLauncherVersion = null;
            settings.LauncherVersion = UpdateService.CurrentLauncherVersion;
            Json.Write(paths.SettingsFile, settings);
        }
        catch { /* 设置文件不可写时忽略 */ }
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }
}
