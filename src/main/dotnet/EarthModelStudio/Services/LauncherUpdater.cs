using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EarthModelStudio.Services;

// ===========================================================================
// 启动器更新模块（唯一实现）
//
// 需求（按用户）：
//   · 只认 GitCode Release 的 latest 版本号（tag），文件名格式：
//       GC-<版本>-<系统>-<架构>.exe / .app
//       例 GC-1.0.3-win-amd64.exe / GC-1.0.3-mac-arm64.app
//   · 当前运行版本与 latest 不同 => 提示更新；相同 => 无需更新
//   · 下载的新版解压到「当前运行目录」，与旧版并列；.GC 完全不动
//   · 更新完成后：删除同系统同架构、版本号 != latest 的其它 GC-* 文件；
//     异系统 / 异架构的文件一律不碰
//   · 一切落盘内容（脚本、标记）只用相对路径，绝不写绝对路径
// ===========================================================================

public sealed class UpdateProgressInfo
{
    public double Value { get; init; }
    public string Stage { get; init; } = "";
}

public enum UpdateRunState { UpToDate, Ready, Failed }

public sealed class UpdateRunResult
{
    public UpdateRunState State { get; init; }
    public string Version { get; init; } = "";
    public string Message { get; init; } = "";
    public string? PackageName { get; init; }
    public string? NewAppPath { get; init; }
}

/// <summary>下载 / 检查 / 应用启动器更新的唯一入口。</summary>
public static class LauncherUpdater
{
    /// <summary>当前运行程序的版本号（来自程序集）。</summary>
    public static string RunningVersion => UpdateService.CurrentLauncherVersion;

    /// <summary>当前 RID，如 osx-arm64。</summary>
    public static string CurrentRid => UpdateService.CurrentRid;

    /// <summary>文件名里用的「系统-架构」标签，如 mac-arm64 / win-amd64。</summary>
    public static string PlatformLabel => CurrentRid switch
    {
        "win-x64" => "win-amd64",
        "win-arm64" => "win-arm64",
        "osx-arm64" => "mac-arm64",
        "osx-x64" => "mac-x64",
        "linux-x64" => "linux-amd64",
        _ => CurrentRid
    };

    // ---------------------------------------------------------------- 检查

    /// <summary>
    /// 检查 Release latest 版本。返回 latest 版本号；若与当前运行版本相同返回 null。
    /// 只比版本号，不比 sha256。
    /// </summary>
    public static async Task<string?> CheckAsync(IModelRemote remote, CancellationToken ct = default)
    {
        try
        {
            var version = await remote.GetVersionAsync(ct);
            if (version == null) return null;
            var latest = (version.Launcher.Version ?? "").Trim();
            if (latest.Length == 0) return null;
            return string.Equals(latest, RunningVersion, StringComparison.OrdinalIgnoreCase)
                ? null
                : latest;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"检查更新失败: {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------- 下载

    /// <summary>
    /// 下载 latest 版本到当前运行目录（与旧版并列）。
    /// 返回新版包名，供后续替换脚本使用。
    /// </summary>
    public static async Task<UpdateRunResult> DownloadAsync(
        AppPaths paths, IModelRemote remote,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report(new UpdateProgressInfo { Value = 0.05, Stage = "正在读取最新版本" });
            var version = await remote.GetVersionAsync(ct);
            if (version == null) return Fail("无法读取 Release 版本清单");

            var latest = (version.Launcher.Version ?? "").Trim();
            var rid = CurrentRid;
            if (!version.Launcher.Packages.TryGetValue(rid, out var pkg) &&
                !version.Launcher.Packages.TryGetValue("default", out pkg))
                return Fail($"该平台（{rid}）没有对应发布包");

            progress?.Report(new UpdateProgressInfo { Value = 0.20, Stage = $"正在下载 v{latest}" });

            var bytes = await remote.DownloadPackageAsync(pkg!, ct);
            if (bytes == null || bytes.Length == 0) return Fail("下载失败：发布包为空");

            // sha256 校验（Release 提供时）
            if (!string.IsNullOrWhiteSpace(pkg!.Sha256))
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actual, pkg.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    return Fail("下载文件校验失败（sha256 不符）");
            }

            progress?.Report(new UpdateProgressInfo { Value = 0.60, Stage = "正在解压到当前目录" });

            // 解压到临时中转目录，再搬到当前运行目录
            var installDir = paths.InstallDirectory;
            Directory.CreateDirectory(installDir);
            var staging = Path.Combine(paths.CacheRoot, "launcher-staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            var zip = Path.Combine(paths.CacheRoot, "launcher-" + Safe(latest) + ".zip");
            await File.WriteAllBytesAsync(zip, bytes, ct);
            try { ZipSafety.ExtractToDirectory(zip, staging); }
            finally { try { File.Delete(zip); } catch { } }

            // 只展平「单个非 .app 顶层目录」的 zip
            FlattenStaging(staging);

            progress?.Report(new UpdateProgressInfo { Value = 0.80, Stage = "正在安装新版本" });

            var packageName = MoveToInstallDir(staging, installDir, latest);
            try { Directory.Delete(staging, true); } catch { }
            if (packageName == null) return Fail("解压结果里没有找到新版启动器");

            // 写标记（纯文本，脚本读它）：只含相对信息，无绝对路径
            Directory.CreateDirectory(paths.UpdateRoot);
            File.WriteAllText(Path.Combine(paths.UpdateRoot, "update.txt"),
                $"NAME={packageName}\nVERSION={latest}\nPLATFORM={PlatformLabel}\n");

            progress?.Report(new UpdateProgressInfo { Value = 1.0, Stage = "下载完成" });

            return new UpdateRunResult
            {
                State = UpdateRunState.Ready,
                Version = latest,
                Message = $"v{latest} 已下载到当前目录",
                PackageName = packageName,
                NewAppPath = Path.Combine(installDir, packageName)
            };
        }
        catch (OperationCanceledException) { return Fail("已取消"); }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static UpdateRunResult Fail(string msg) => new()
    {
        State = UpdateRunState.Failed,
        Message = msg
    };

    // ---------------------------------------------------------------- 应用（下次启动）

    /// <summary>
    /// 生成替换脚本并启动。脚本等本进程退出后：
    ///   启动新版 -> 删除同平台且版本号 != latest 的 GC-* 文件 -> 清理标记。
    /// 返回 true 表示调用方应立即退出进程。
    /// </summary>
    public static bool ScheduleApply(AppPaths paths, string packageName)
    {
        var marker = Path.Combine(paths.UpdateRoot, "update.txt");
        if (!File.Exists(marker)) return false;

        // 安全阀：绝不在源码目录执行替换（防止误删源码）
        if (File.Exists(Path.Combine(paths.InstallDirectory, "EarthModelStudio.sln")))
        {
            Console.Error.WriteLine("源码版：新版已下载，但拒绝在源码目录执行替换");
            return false;
        }

        try
        {
            var isWin = OperatingSystem.IsWindows();
            var script = Path.Combine(paths.UpdateRoot, isWin ? "apply.bat" : "apply.sh");
            var content = isWin ? WindowsScript() : UnixScript();
            File.WriteAllText(script, content, new System.Text.UTF8Encoding(false));

            if (!isWin)
            {
                try
                {
                    File.SetUnixFileMode(script,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch { }
            }

            var psi = isWin
                ? new System.Diagnostics.ProcessStartInfo("cmd.exe")
                { UseShellExecute = false, CreateNoWindow = true }
                : new System.Diagnostics.ProcessStartInfo("/bin/sh")
                { UseShellExecute = false, CreateNoWindow = true };
            if (isWin) psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(script);
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"启动替换脚本失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>启动时若有待应用更新，安排脚本并让 Main 直接退出。</summary>
    public static bool TryApplyPending(AppPaths paths)
    {
        if (paths.IsDevelopment) return false;
        if (Environment.GetEnvironmentVariable("EMS_SELFTEST") == "1") return false;
        if (Environment.GetEnvironmentVariable("EMS_NO_SELF_UPDATE") == "1") return false;

        var marker = Path.Combine(paths.UpdateRoot, "update.txt");
        if (!File.Exists(marker)) return false;

        var name = ReadMarker(marker, "NAME");
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!File.Exists(Path.Combine(paths.InstallDirectory, name!)) &&
            !Directory.Exists(Path.Combine(paths.InstallDirectory, name!)))
            return false;   // 新版本不在了，放弃

        return ScheduleApply(paths, name!);
    }

    // ---------------------------------------------------------------- 脚本（全部相对路径）

    private static string UnixScript()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine("#!/bin/sh");
        s.AppendLine("# script lives in .GC/update/ ; install dir is two levels up");
        s.AppendLine("HERE=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)");
        s.AppendLine("DST=$(CDPATH= cd -- \"$HERE/../..\" && pwd)");
        s.AppendLine("MARKER=\"$HERE/update.txt\"");
        s.AppendLine();
        s.AppendLine("# read marker");
        s.AppendLine("NEWNAME=$(sed -n 's/^NAME=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("NEWVER=$(sed -n 's/^VERSION=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("NEWPLAT=$(sed -n 's/^PLATFORM=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("if [ -z \"$NEWNAME\" ] || [ -z \"$NEWVER\" ]; then");
        s.AppendLine("  echo \"marker incomplete, abort\"; rm -f \"$HERE\"/*.sh 2>/dev/null; exit 1");
        s.AppendLine("fi");
        s.AppendLine("NEW=\"$DST/$NEWNAME\"");
        s.AppendLine("if [ ! -e \"$NEW\" ]; then");
        s.AppendLine("  echo \"new package missing, abort\"; rm -f \"$HERE\"/*.sh 2>/dev/null; exit 1");
        s.AppendLine("fi");
        s.AppendLine();
        s.AppendLine("# wait for old process to exit (this script is started by it)");
        s.AppendLine("sleep 2");
        s.AppendLine();
        s.AppendLine("# launch new version first");
        s.AppendLine("if [ -d \"$NEW\" ]; then open \"$NEW\" 2>/dev/null &");
        s.AppendLine("elif [ -x \"$NEW\" ]; then \"$NEW\" >/dev/null 2>&1 &");
        s.AppendLine("fi");
        s.AppendLine();
        s.AppendLine("# delete old GC-* files: SAME platform only, version != latest");
        s.AppendLine("# files of OTHER platforms are NEVER touched");
        s.AppendLine("for item in \"$DST\"/GC-*.app \"$DST\"/GC-*.exe; do");
        s.AppendLine("  [ -e \"$item\" ] || continue");
        s.AppendLine("  [ \"$item\" = \"$NEW\" ] && continue");
        s.AppendLine("  base=$(basename \"$item\")");
        s.AppendLine("  stem=${base#GC-}");
        s.AppendLine("  case \"$base\" in");
        s.AppendLine("    *.app) stem=${stem%.app} ;;");
        s.AppendLine("    *.exe) stem=${stem%.exe} ;;");
        s.AppendLine("    *) continue ;;");
        s.AppendLine("  esac");
        s.AppendLine("  ver=${stem%%-*}");
        s.AppendLine("  rest=${stem#*-}");
        s.AppendLine("  plat=\"${rest%-*}-${rest##*-}\"");
        s.AppendLine("  # skip other platforms entirely");
        s.AppendLine("  [ \"$plat\" != \"$NEWPLAT\" ] && continue");
        s.AppendLine("  # same platform: keep only latest version");
        s.AppendLine("  if [ \"$ver\" = \"$NEWVER\" ]; then");
        s.AppendLine("    echo \"keep: $base\"");
        s.AppendLine("  else");
        s.AppendLine("    echo \"remove (version $ver != $NEWVER): $base\"");
        s.AppendLine("    rm -rf \"$item\" 2>/dev/null");
        s.AppendLine("  fi");
        s.AppendLine("done");
        s.AppendLine();
        s.AppendLine("rm -f \"$MARKER\"");
        s.AppendLine("rm -f \"$HERE\"/*.sh 2>/dev/null");
        return s.ToString();
    }

    private static string WindowsScript()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine("@echo off");
        s.AppendLine("chcp 65001 >nul 2>&1");
        s.AppendLine("set \"HERE=%~dp0\"");
        s.AppendLine("set \"DST=%~dp0..\\..\"");
        s.AppendLine("set \"MARKER=%~dp0update.txt\"");
        s.AppendLine("timeout /t 2 /nobreak >nul");
        s.AppendLine();
        s.AppendLine("set \"NEWNAME=\"");
        s.AppendLine("set \"NEWVER=\"");
        s.AppendLine("set \"NEWPLAT=\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"NAME=\" \"%MARKER%\" 2^>nul') do set \"NEWNAME=%%A\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"VERSION=\" \"%MARKER%\" 2^>nul') do set \"NEWVER=%%A\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"PLATFORM=\" \"%MARKER%\" 2^>nul') do set \"NEWPLAT=%%A\"");
        s.AppendLine("if \"%NEWNAME%\"==\"\" (echo marker incomplete, abort & del /Q \"%~f0\" >nul 2>&1 & exit /b 1)");
        s.AppendLine("if not exist \"%DST%\\%NEWNAME%\" (echo new package missing, abort & del /Q \"%~f0\" >nul 2>&1 & exit /b 1)");
        s.AppendLine();
        s.AppendLine("rem launch new version");
        s.AppendLine("start \"\" \"%DST%\\%NEWNAME%\"");
        s.AppendLine();
        s.AppendLine("rem delete old GC-* files: SAME platform only, version != latest");
        s.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"^");
        s.AppendLine("$dst='%DST%'; $new='%NEWNAME%'; $ver='%NEWVER%'; $plat='%NEWPLAT%';^");
        s.AppendLine("Get-ChildItem -LiteralPath $dst -Filter 'GC-*' | ForEach-Object {^");
        s.AppendLine("  $n=$_.Name; if($n -eq $new){return};^");
        s.AppendLine("  $m=[regex]::Match($n,'^GC-(.+?)-([^-]+)-([^-]+)\\.(app|exe)$');^");
        s.AppendLine("  if(-not $m.Success){return};^");
        s.AppendLine("  $fver=$m.Groups[1].Value; $fplat=$m.Groups[2].Value+'-'+$m.Groups[3].Value;^");
        s.AppendLine("  if($fplat -ne $plat){return};^");
        s.AppendLine("  if($fver -eq $ver){ Write-Host \\\"keep: $n\\\" }^");
        s.AppendLine("  else { Write-Host \\\"remove: $n\\\"; Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }^");
        s.AppendLine("}\"");
        s.AppendLine();
        s.AppendLine("del /Q \"%MARKER%\" >nul 2>&1");
        s.AppendLine("del /Q \"%~f0\" >nul 2>&1");
        return s.ToString();
    }

    // ---------------------------------------------------------------- 内部工具

    /// <summary>展平只有一个顶层目录、且不是 .app 的 zip。</summary>
    private static void FlattenStaging(string dir)
    {
        var entries = Directory.GetFileSystemEntries(dir);
        if (entries.Length != 1 || !Directory.Exists(entries[0])) return;
        var inner = entries[0];
        if (Path.GetFileName(inner).EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return;

        var flat = dir + "-flat";
        if (Directory.Exists(flat)) Directory.Delete(flat, true);
        Directory.Move(inner, flat);
        Directory.Delete(dir, true);
        Directory.Move(flat, dir);
    }

    /// <summary>
    /// 把 staging 里的新版搬到当前目录，与旧版并列。
    /// 返回新版包名（GC-&lt;版本&gt;-&lt;平台&gt;.app/.exe）。.GC 绝不覆盖。
    /// </summary>
    private static string? MoveToInstallDir(string staging, string installDir, string latest)
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(staging))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, ".GC", StringComparison.OrdinalIgnoreCase)) continue;
                var dest = Path.Combine(installDir, name);

                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                else if (File.Exists(dest)) File.Delete(dest);

                if (Directory.Exists(entry)) Directory.Move(entry, dest);
                else File.Move(entry, dest);

                // 保证 .app 里可执行文件可执行
                if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(dest))
                {
                    var macOs = Path.Combine(dest, "Contents", "MacOS");
                    if (Directory.Exists(macOs))
                    {
                        try
                        {
                            foreach (var f in Directory.GetFiles(macOs, "*", SearchOption.TopDirectoryOnly))
                            {
                                var mode = File.GetUnixFileMode(f);
                                File.SetUnixFileMode(f, mode | UnixFileMode.UserExecute);
                            }
                        }
                        catch { }
                    }
                    return name;
                }
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return name;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"安装新版失败: {ex.Message}");
        }
        return null;
    }

    private static string? ReadMarker(string file, string key)
    {
        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..].Trim();
            }
        }
        catch { }
        return null;
    }

    private static string Safe(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Replace('/', '-').Replace(' ', '-');
    }
}
