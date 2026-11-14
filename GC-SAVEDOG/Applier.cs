using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GC.Services;

namespace GC.Savedog;

internal sealed class ApplyOptions
{
    public string UpdateDir { get; set; } = "";
    public string? InstallDir { get; set; }
    public int WaitPid { get; set; }
    public int TimeoutSeconds { get; set; } = 45;
    public bool RequireSignature { get; set; } = true;
    public bool DryRun { get; set; }
    public bool Force { get; set; }
}

/// <summary>
/// 应用一次启动器更新：验签 → 校验 → 原子替换 → 启动 → 看护 → 成功清场 / 失败回滚。
/// 任何一步失败都不会留下「删了旧版又没有新版」的状态。
/// </summary>
internal sealed class Applier
{
    private static readonly Regex ArtifactPattern = new(
        @"^GC-(?<ver>.+?)-(?<plat>[^\-]+-[^\-]+)\.(app|exe)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ApplyOptions _options;
    private readonly Log _log;

    public Applier(ApplyOptions options, Log log)
    {
        _options = options;
        _log = log;
    }

    public int Run()
    {
        var updateDir = Path.GetFullPath(_options.UpdateDir);
        var requestPath = Path.Combine(updateDir, SavedogFiles.ApplyRequest);

        if (!File.Exists(requestPath))
            return Fail(updateDir, "找不到更新申请 " + SavedogFiles.ApplyRequest, 3);

        var request = Json.Read<SavedogRequest>(requestPath);
        if (request == null || string.IsNullOrWhiteSpace(request.Version))
            return Fail(updateDir, "更新申请内容不完整", 3);

        if (!ValidName(request.PackageName))
            return Fail(updateDir, "更新包名不合法: " + request.PackageName, 3);

        _log.Info($"开始应用 v{request.Version}（{request.Rid}，{request.PackageName}）");

        // 1. 等旧进程退出（否则 Windows 上文件还被占用）
        if (_options.WaitPid > 0 && !WaitForExit(_options.WaitPid, TimeSpan.FromSeconds(60)))
            _log.Warn($"等待进程 {_options.WaitPid} 退出超时，继续尝试");

        // 2. 安装目录定位与安全检查
        var installDir = string.IsNullOrWhiteSpace(_options.InstallDir)
            ? Path.GetFullPath(Path.Combine(updateDir, request.InstallRelative))
            : Path.GetFullPath(_options.InstallDir!);

        if (!Directory.Exists(installDir))
            return Fail(updateDir, "安装目录不存在: " + installDir, 4);

        if (!_options.Force && File.Exists(Path.Combine(installDir, "GC.sln")))
            return Fail(updateDir, "检测到源码目录，拒绝在源码树里替换程序", 4);

        // 3. 二次验签：不信任「启动器说它验过了」
        var require = request.RequireSignature && SignatureKeyring.Enforced;
        var manifestPath = Path.Combine(updateDir, request.ManifestPath);
        var manifestText = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
        var verification = ManifestSignature.Verify(manifestText, require);

        if (verification.IsRejected)
        {
            _log.Error($"清单验签失败（{verification.Trust}）：{verification.Error}");
            return Refuse(updateDir, request, verification);
        }
        _log.Info($"清单可信度：{verification.Trust}" +
                  (verification.KeyId != null ? $"（{verification.KeyId}）" : ""));

        // 4. 校验更新包
        var zipPath = Path.Combine(updateDir, request.PackageZip);
        if (!File.Exists(zipPath))
            return Fail(updateDir, "更新包不存在: " + request.PackageZip, 5);

        var zipInfo = new FileInfo(zipPath);
        if (request.PackageSize > 0 && zipInfo.Length != request.PackageSize)
            return Fail(updateDir, $"更新包体积不符（期望 {request.PackageSize}，实际 {zipInfo.Length}）", 5);

        var actualSha = ContentHash.Sha256File(zipPath);
        if (!string.IsNullOrWhiteSpace(request.PackageSha256) &&
            !string.Equals(actualSha, request.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(updateDir, "更新包 sha256 与清单不符，已丢弃", 5);
        }
        if (string.IsNullOrWhiteSpace(request.PackageSha256))
            _log.Warn("清单没有提供 sha256，仅依赖签名与解压校验");

        SafeArchive.Inspection inspection;
        try
        {
            inspection = SafeArchive.Inspect(zipPath);
            _log.Info($"更新包检查通过：{inspection.Entries} 个条目，" +
                      $"{inspection.UncompressedBytes / 1024 / 1024} MB，压缩比 {inspection.Ratio:0.0}:1");
        }
        catch (Exception ex)
        {
            return Fail(updateDir, "更新包安全性检查失败: " + ex.Message, 5);
        }

        // 5. 解压到暂存区（与安装目录同卷，后续 rename 才是原子的）
        var stage = Path.Combine(updateDir, "stage");
        ClearDirectory(stage);
        try
        {
            SafeArchive.ExtractToDirectory(zipPath, stage);
            FlattenSingleDirectory(stage);
        }
        catch (Exception ex)
        {
            ClearDirectory(stage);
            return Fail(updateDir, "解压失败: " + ex.Message, 6);
        }

        var artifact = FindArtifact(stage, request.PackageName);
        if (artifact == null)
        {
            ClearDirectory(stage);
            return Fail(updateDir, "更新包里没有找到新版程序（" + request.PackageName + "）", 6);
        }

        var target = Path.Combine(installDir, Path.GetFileName(artifact));
        var alreadyInstalled = File.Exists(target) || Directory.Exists(target);

        if (_options.DryRun)
        {
            _log.Info($"[dry-run] 校验全部通过，将安装到 {target}");
            ClearDirectory(stage);
            return 0;
        }

        if (alreadyInstalled)
        {
            // 同一版本重复下发：当成已安装，避免反复覆盖正在运行的文件
            _log.Info("目标版本已存在，视为已安装");
            ClearDirectory(stage);
            TryDelete(zipPath);
            TryDelete(requestPath);
            WriteState(updateDir, new SavedogState
            {
                Status = "applied",
                Version = request.Version,
                PackageName = Path.GetFileName(artifact),
                Verified = true,
                Message = "目标版本已存在，无需替换"
            });
            return 0;
        }

        // 6. 移入安装目录（不覆盖任何已有文件）
        try
        {
            Directory.CreateDirectory(installDir);
            if (Directory.Exists(artifact)) Directory.Move(artifact, target);
            else File.Move(artifact, target);
            _log.Info("新版已就位: " + Path.GetFileName(target));
        }
        catch (Exception ex)
        {
            ClearDirectory(stage);
            return Fail(updateDir, "移动到安装目录失败: " + ex.Message, 7);
        }

        // 7. 启动并看护
        var previous = ReadLastGood(updateDir);
        StartupHealth.Clear(updateDir);

        Process? child;
        try
        {
            child = Launch(target);
        }
        catch (Exception ex)
        {
            child = null;
            _log.Error("启动新版失败: " + ex.Message);
        }

        var healthy = child != null && WaitForHealth(updateDir, request.Version, child,
            TimeSpan.FromSeconds(_options.TimeoutSeconds));

        if (healthy)
        {
            _log.Info($"v{request.Version} 启动正常，更新完成");
            CleanupOldVersions(installDir, Path.GetFileName(target), request.PlatformLabel);
            WriteLastGood(updateDir, request.Version, Path.GetFileName(target));
            WriteState(updateDir, new SavedogState
            {
                Status = "applied",
                Version = request.Version,
                PreviousVersion = previous?.Version,
                PackageName = Path.GetFileName(target),
                Verified = true,
                Message = "更新已应用"
            });
            ClearDirectory(stage);
            TryDelete(zipPath);
            TryDelete(requestPath);
            return 0;
        }

        // 8. 回滚
        _log.Error($"v{request.Version} 启动后未通过健康检查（{_options.TimeoutSeconds}s），开始回滚");
        TryKill(child);
        var removed = RemoveNewVersion(target);
        _log.Info(removed ? "已移除未通过检查的新版" : "新版文件被占用，未能完全移除");

        var restored = TryLaunchPrevious(installDir, updateDir, previous, request.PlatformLabel);
        WriteState(updateDir, new SavedogState
        {
            Status = "rolledback",
            Version = request.Version,
            PreviousVersion = previous?.Version,
            PackageName = Path.GetFileName(target),
            Verified = true,
            Message = restored
                ? "新版本未通过启动检查，已回滚到上一个版本"
                : "新版本未通过启动检查，已移除；未找到可回滚的旧版本，请手动重新安装"
        });

        ClearDirectory(stage);
        TryDelete(zipPath);
        TryDelete(requestPath);
        return 8;
    }

    // ------------------------------------------------------------------ 校验辅助

    private static bool ValidName(string name)
        => name.Length > 0 && name.Length < 200 &&
           name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0 &&
           (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited) return true;
            }
            catch (ArgumentException) { return true; }   // 进程已不存在
            catch { return true; }
            Thread.Sleep(200);
        }
        return false;
    }

    private static void FlattenSingleDirectory(string stage)
    {
        var entries = Directory.GetFileSystemEntries(stage);
        if (entries.Length != 1 || !Directory.Exists(entries[0])) return;
        var inner = entries[0];
        if (Path.GetFileName(inner).EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return;

        var temp = stage + "-flat";
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.Move(inner, temp);
        Directory.Delete(stage, true);
        Directory.Move(temp, stage);
    }

    private static string? FindArtifact(string stage, string packageName)
    {
        var direct = Path.Combine(stage, packageName);
        if (File.Exists(direct) || Directory.Exists(direct)) return direct;

        foreach (var entry in Directory.GetFileSystemEntries(stage))
        {
            var name = Path.GetFileName(entry);
            if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(entry))
                return entry;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(entry))
                return entry;
        }
        return null;
    }

    // ------------------------------------------------------------------ 启动与看护

    private Process? Launch(string target)
    {
        if (Directory.Exists(target))
        {
            // .app：优先直接启动内部可执行文件，这样才能拿到 pid 做看护
            var macOs = Path.Combine(target, "Contents", "MacOS");
            if (Directory.Exists(macOs))
            {
                var exe = Directory.GetFiles(macOs)
                    .OrderByDescending(f => Path.GetFileName(f) == "GC")
                    .FirstOrDefault();
                if (exe != null)
                {
                    MakeExecutable(exe);
                    return StartProcess(exe, Path.GetDirectoryName(exe)!);
                }
            }
            MakeExecutable(Path.Combine(target, "Contents", "MacOS", "GC"));
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(target);
            return Process.Start(psi);
        }

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (!OperatingSystem.IsWindows()) MakeExecutable(target);
            return StartProcess(target, Path.GetDirectoryName(target)!);
        }
        return null;
    }

    private static Process? StartProcess(string exe, string workingDirectory)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        return Process.Start(psi);
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            if (!File.Exists(path)) return;
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute |
                                          UnixFileMode.GroupExecute |
                                          UnixFileMode.OtherExecute);
        }
        catch { }
    }

    private bool WaitForHealth(string updateDir, string version, Process child, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var health = StartupHealth.Read(updateDir);
            if (health != null && health.Ok && SameVersion(health.Version, version))
            {
                _log.Info($"收到健康标记（pid {health.Pid}）");
                return true;
            }

            if (child.HasExited)
            {
                _log.Error($"新版进程已退出（退出码 {child.ExitCode}）");
                return false;
            }
            Thread.Sleep(250);
        }

        // 超时但进程还活着：可能是尚未写入健康标记的旧构建，谨慎放行
        if (!child.HasExited)
        {
            _log.Warn("未收到健康标记，但进程仍在运行，按成功处理");
            return true;
        }
        return false;
    }

    private static bool SameVersion(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string version)
    {
        var trimmed = version.Trim().TrimStart('v', 'V');
        var parts = trimmed.Split('.');
        var numbers = new List<string>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var n)) break;
            numbers.Add(n.ToString());
        }
        while (numbers.Count > 0 && numbers[^1] == "0") numbers.RemoveAt(numbers.Count - 1);
        return numbers.Count == 0 ? trimmed : string.Join('.', numbers);
    }

    private static void TryKill(Process? process)
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    // ------------------------------------------------------------------ 清理 / 回滚

    private void CleanupOldVersions(string installDir, string keepName, string platformLabel)
    {
        foreach (var entry in Directory.GetFileSystemEntries(installDir))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, keepName, StringComparison.OrdinalIgnoreCase)) continue;

            var match = ArtifactPattern.Match(name);
            if (!match.Success) continue;

            var platform = match.Groups["plat"].Value;
            if (!string.Equals(platform, platformLabel, StringComparison.OrdinalIgnoreCase))
            {
                _log.Info($"保留其它平台版本: {name}");
                continue;
            }

            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, true);
                else File.Delete(entry);
                _log.Info("清理旧版本: " + name);
            }
            catch (Exception ex)
            {
                _log.Warn($"清理旧版本失败 {name}: {ex.Message}");
            }
        }
    }

    private bool RemoveNewVersion(string target)
    {
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            else if (File.Exists(target)) File.Delete(target);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn("移除新版失败: " + ex.Message);
            return false;
        }
    }

    private bool TryLaunchPrevious(string installDir, string updateDir, LastGoodVersion? lastGood,
        string platformLabel)
    {
        // 优先用上次成功的版本；否则挑一个同平台的其它版本
        var candidates = new List<string>();
        if (lastGood != null && !string.IsNullOrWhiteSpace(lastGood.PackageName))
            candidates.Add(Path.Combine(installDir, lastGood.PackageName));

        foreach (var entry in Directory.GetFileSystemEntries(installDir))
        {
            var name = Path.GetFileName(entry);
            var match = ArtifactPattern.Match(name);
            if (!match.Success) continue;
            if (!string.Equals(match.Groups["plat"].Value, platformLabel, StringComparison.OrdinalIgnoreCase))
                continue;
            candidates.Add(entry);
        }

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) continue;
            try
            {
                Launch(candidate);
                _log.Info("已回滚启动: " + Path.GetFileName(candidate));
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn($"回滚启动失败 {Path.GetFileName(candidate)}: {ex.Message}");
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ 状态文件

    private void WriteState(string updateDir, SavedogState state)
    {
        try { Json.Write(Path.Combine(updateDir, SavedogFiles.State), state); }
        catch (Exception ex) { _log.Warn("写状态失败: " + ex.Message); }
    }

    private int Fail(string updateDir, string message, int code)
    {
        _log.Error(message);
        WriteState(updateDir, new SavedogState { Status = "failed", Message = message });
        return code;
    }

    private int Refuse(string updateDir, SavedogRequest request, ManifestVerification verification)
    {
        WriteState(updateDir, new SavedogState
        {
            Status = "refused",
            Version = request.Version,
            PackageName = request.PackageName,
            Verified = false,
            Message = $"清单验签失败：{verification.Error}"
        });
        return 2;
    }

    private static LastGoodVersion? ReadLastGood(string updateDir)
        => Json.Read<LastGoodVersion>(Path.Combine(updateDir, SavedogFiles.LastGood));

    private static void WriteLastGood(string updateDir, string version, string packageName)
    {
        try
        {
            Json.Write(Path.Combine(updateDir, SavedogFiles.LastGood), new LastGoodVersion
            {
                Version = version,
                PackageName = packageName
            });
        }
        catch { }
    }

    private static void ClearDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
