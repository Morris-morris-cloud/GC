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
    private readonly ApplyOptions _options;
    private readonly Log _log;

    /// <summary>发布包里的启动器文件名：GC-1.0.9-win-amd64.exe。</summary>
    private static readonly Regex VersionedExePattern = new(
        @"^(?:GC|GCL)-\d+(?:\.\d+)+-[A-Za-z0-9]+-[A-Za-z0-9]+\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
            return Fail(updateDir, "找不到更新申请 " + SavedogFiles.ApplyRequest, 3,
                       "Update request not found: " + SavedogFiles.ApplyRequest);

        var request = Json.Read<SavedogRequest>(requestPath);
        if (request == null || string.IsNullOrWhiteSpace(request.Version))
            return Fail(updateDir, "更新申请内容不完整", 3, "The update request is incomplete");

        if (!ValidName(request.PackageName))
            return Fail(updateDir, "更新包名不合法: " + request.PackageName, 3,
                       "Invalid package name: " + request.PackageName);

        _log.Info($"开始应用 v{request.Version}（{request.Rid}，{request.PackageName}）",
                  $"Starting apply of v{request.Version} ({request.Rid}, {request.PackageName})");

        // 1. 等旧进程退出（否则 Windows 上文件还被占用）
        if (_options.WaitPid > 0 && !WaitForExit(_options.WaitPid, TimeSpan.FromSeconds(60)))
            _log.Warn($"等待进程 {_options.WaitPid} 退出超时，继续尝试",
                      $"Timed out waiting for process {_options.WaitPid} to exit; continuing anyway");

        // 2. 安装目录定位与安全检查
        var installDir = string.IsNullOrWhiteSpace(_options.InstallDir)
            ? Path.GetFullPath(Path.Combine(updateDir, request.InstallRelative))
            : Path.GetFullPath(_options.InstallDir!);

        if (!Directory.Exists(installDir))
            return Fail(updateDir, "安装目录不存在: " + installDir, 4,
                       "Install directory does not exist: " + installDir);

        if (!_options.Force && File.Exists(Path.Combine(installDir, "GC.sln")))
            return Fail(updateDir, "检测到源码目录，拒绝在源码树里替换程序", 4,
                       "Source tree detected; refusing to replace files inside it");

        // 3. 二次验签：不信任「启动器说它验过了」
        var require = request.RequireSignature && SignatureKeyring.Enforced;
        var manifestPath = Path.Combine(updateDir, request.ManifestPath);
        var manifestText = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
        var verification = ManifestSignature.Verify(manifestText, require);

        if (verification.IsRejected)
        {
            _log.Error($"清单验签失败（{verification.Trust}）：{verification.Error}",
                       $"Manifest signature check failed ({verification.Trust}): {verification.Error}");
            return Refuse(updateDir, request, verification);
        }
        _log.Info($"清单可信度：{verification.Trust}" +
                  (verification.KeyId != null ? $"（{verification.KeyId}）" : ""),
                  $"Manifest trust: {verification.Trust}" +
                  (verification.KeyId != null ? $" ({verification.KeyId})" : ""));

        // 4. 校验更新包
        var zipPath = Path.Combine(updateDir, request.PackageZip);
        if (!File.Exists(zipPath))
            return Fail(updateDir, "更新包不存在: " + request.PackageZip, 5,
                       "Package not found: " + request.PackageZip);

        var zipInfo = new FileInfo(zipPath);
        if (request.PackageSize > 0 && zipInfo.Length != request.PackageSize)
            return Fail(updateDir, $"更新包体积不符（期望 {request.PackageSize}，实际 {zipInfo.Length}）", 5,
                       $"Package size mismatch (expected {request.PackageSize}, got {zipInfo.Length})");

        var actualSha = ContentHash.Sha256File(zipPath);
        if (!string.IsNullOrWhiteSpace(request.PackageSha256) &&
            !string.Equals(actualSha, request.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(updateDir, "更新包 sha256 与清单不符，已丢弃", 5,
                       "Package sha256 does not match the manifest; discarded");
        }
        if (string.IsNullOrWhiteSpace(request.PackageSha256))
            _log.Warn("清单没有提供 sha256，仅依赖签名与解压校验",
                       "Manifest has no sha256; relying on the signature and extraction checks only");

        SafeArchive.Inspection inspection;
        try
        {
            inspection = SafeArchive.Inspect(zipPath);
            _log.Info($"更新包检查通过：{inspection.Entries} 个条目，" +
                      $"{inspection.UncompressedBytes / 1024 / 1024} MB，压缩比 {inspection.Ratio:0.0}:1",
                      $"Package inspection passed: {inspection.Entries} entries, " +
                      $"{inspection.UncompressedBytes / 1024 / 1024} MB, ratio {inspection.Ratio:0.0}:1");
        }
        catch (Exception ex)
        {
            return Fail(updateDir, "更新包安全性检查失败: " + ex.Message, 5,
                       "Package safety check failed: " + ex.Message);
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
            return Fail(updateDir, "解压失败: " + ex.Message, 6, "Extraction failed: " + ex.Message);
        }

        var artifact = FindArtifact(stage, request.PackageName);
        if (artifact == null)
        {
            ClearDirectory(stage);
            return Fail(updateDir, "更新包里没有找到新版程序（" + request.PackageName + "）", 6,
                       "No new launcher found in the package (" + request.PackageName + ")");
        }

        // 主程序：优先用清单里的包名，否则按已知名字挑一个（发布包是整目录，
        // 里面同时有 GC-UI.exe / GC.UI.Server.exe / GC-SAVEDOG.exe，不能瞎猜第一个）。
        var mainName = Path.GetFileName(artifact);
        var mainTarget = Path.Combine(installDir, mainName);

        if (_options.DryRun)
        {
            _log.Info($"[dry-run] 校验全部通过，将整目录安装到 {installDir}" +
                      $"（{Directory.GetFileSystemEntries(stage).Length} 个顶层条目）",
                      $"[dry-run] All checks passed; would install the whole directory to {installDir} " +
                      $"({Directory.GetFileSystemEntries(stage).Length} top-level entries)");
            ClearDirectory(stage);
            return 0;
        }

        var previous = ReadLastGood(updateDir);
        if (previous != null && SameVersion(previous.Version, request.Version) &&
            (File.Exists(mainTarget) || Directory.Exists(mainTarget)))
        {
            // 同一版本重复下发：当成已安装，避免反复覆盖正在运行的文件
            _log.Info($"v{request.Version} 已安装且上次启动正常，视为已安装",
                          $"v{request.Version} is already installed and started fine last time; treating as installed");
            ClearDirectory(stage);
            TryDelete(zipPath);
            TryDelete(requestPath);
            WriteState(updateDir, new SavedogState
            {
                Status = "applied",
                Version = request.Version,
                PackageName = mainName,
                Verified = true,
                Message = "目标版本已存在，无需替换"
            });
            return 0;
        }

        // 6. 整目录替换：旧内容先搬到 backup，新内容就位；失败可原样还原。
        //    只动程序文件，.GC（模型库 / 设置 / 更新目录）永远不碰。
        var replaced = new List<(string Name, string? BackupPath)>();
        if (!ReplaceInstallDirectory(stage, installDir, updateDir, replaced, out var replaceError))
        {
            RestoreFromBackup(installDir, replaced);
            ClearDirectory(stage);
            return Fail(updateDir, "安装失败: " + replaceError, 7, "Installation failed: " + replaceError);
        }
        _log.Info($"新版已就位：{mainName}（替换 {replaced.Count} 个条目）",
                  $"New version in place: {mainName} (replaced {replaced.Count} entries)");

        // 7. 启动并看护
        StartupHealth.Clear(updateDir);

        Process? child;
        try
        {
            child = Launch(mainTarget);
        }
        catch (Exception ex)
        {
            child = null;
            _log.Error("启动新版失败: " + ex.Message,
                       "Failed to start the new version: " + ex.Message);
        }

        var healthy = child != null && WaitForHealth(updateDir, request.Version, child,
            TimeSpan.FromSeconds(_options.TimeoutSeconds));

        if (healthy)
        {
            _log.Info($"v{request.Version} 启动正常，更新完成",
                      $"v{request.Version} started successfully; update complete");
            CleanupBackup(updateDir);
            WriteLastGood(updateDir, request.Version, mainName);
            WriteState(updateDir, new SavedogState
            {
                Status = "applied",
                Version = request.Version,
                PreviousVersion = previous?.Version,
                PackageName = mainName,
                Verified = true,
                Message = "更新已应用"
            });
            ClearDirectory(stage);
            TryDelete(zipPath);
            TryDelete(requestPath);
            return 0;
        }

        // 8. 回滚：撤掉新装的文件，把备份原样搬回
        _log.Error($"v{request.Version} 启动后未通过健康检查（{_options.TimeoutSeconds}s），开始回滚",
                   $"v{request.Version} did not pass the health check after {_options.TimeoutSeconds}s; rolling back");
        TryKill(child);
        var restored = RestoreFromBackup(installDir, replaced);
        _log.Info(restored ? "已还原为更新前的文件" : "部分文件未能还原，请手动重新安装",
                      restored ? "Restored the pre-update files" : "Some files could not be restored; please reinstall manually");

        var relaunched = restored && TryLaunchPrevious(installDir, updateDir, previous, request.PlatformLabel);
        WriteState(updateDir, new SavedogState
        {
            Status = "rolledback",
            Version = request.Version,
            PreviousVersion = previous?.Version,
            PackageName = mainName,
            Verified = true,
            Message = relaunched
                ? "新版本未通过启动检查，已回滚到上一个版本"
                : "新版本未通过启动检查，已移除；未找到可回滚的旧版本，请手动重新安装"
        });

        ClearDirectory(stage);
        TryDelete(zipPath);
        TryDelete(requestPath);
        return 8;
    }

    // ------------------------------------------------------------------ 校验辅助

    /// <summary>
    /// 包名合法性：只允许「一个文件名」，杜绝 `../` 之类的路径穿越。
    ///
    /// 不能只认 .app / .exe —— Linux 的启动器没有扩展名
    /// （GC-1.0.9-linux-amd64），按旧规则会被直接判成非法包名、更新永远失败。
    /// </summary>
    private static bool ValidName(string name)
        => SafeNamePattern.IsMatch(name);

    private static readonly Regex SafeNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,198}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

    /// <summary>
    /// 在解压结果里找主程序。
    ///
    /// 发布包是「整目录」结构（exe + dll + data/），同一层可能有好几个 exe
    /// （GC-UI / GC.UI.Server / GC-SAVEDOG / createdump），必须按固定优先级挑，
    /// 不能取第一个 —— 否则会把守护进程或调试辅助程序当成启动器拉起来。
    /// </summary>
    private static string? FindArtifact(string stage, string packageName)
    {
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            var named = Path.Combine(stage, packageName);
            if (File.Exists(named) || Directory.Exists(named)) return named;
        }

        var entries = Directory.GetFileSystemEntries(stage);
        var byName = entries
            .Where(e => File.Exists(e))
            .OrderByDescending(e => MainExecutablePriority(Path.GetFileName(e)))
            .ThenBy(e => Path.GetFileName(e), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var best = byName.FirstOrDefault(e => MainExecutablePriority(Path.GetFileName(e)) > 0);
        if (best != null) return best;

        // macOS：.app 目录本身就是「可执行单元」
        foreach (var entry in entries)
        {
            if (Directory.Exists(entry) &&
                Path.GetFileName(entry).EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    /// <summary>主程序优先级：数字越大越优先，0 表示不是主程序。</summary>
    private static int MainExecutablePriority(string fileName)
    {
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return 0;

        // 发布包把启动器改名成 GC-<版本>-<系统>-<架构>.exe（GCL- 是预留前缀），先按这个模式认出来。
        if (VersionedExePattern.IsMatch(fileName)) return 110;

        var name = Path.GetFileNameWithoutExtension(fileName);
        return name switch
        {
            "GC-UI" => 100,
            "GC" => 90,
            "GC.UI.Server" => 80,
            // 守护进程 / 调试辅助 / 运行时工具：永远不当主程序
            "GC-SAVEDOG" => 0,
            "createdump" => 0,
            "singlefilehost" => 0,
            _ => 10
        };
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
                _log.Info($"收到健康标记（pid {health.Pid}）", $"Health marker received (pid {health.Pid})");
                return true;
            }

            if (child.HasExited)
            {
                _log.Error($"新版进程已退出（退出码 {child.ExitCode}）",
                           $"The new process exited (code {child.ExitCode})");
                return false;
            }
            Thread.Sleep(250);
        }

        // 超时但进程还活着：可能是尚未写入健康标记的旧构建，谨慎放行
        if (!child.HasExited)
        {
            _log.Warn("未收到健康标记，但进程仍在运行，按成功处理",
                      "No health marker, but the process is still running; treating as success");
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

    // ------------------------------------------------------------------ 整目录安装 / 回滚

    /// <summary>
    /// 把 <paramref name="stage"/> 的内容替换进 <paramref name="installDir"/>。
    ///
    /// 发布包是「整目录」（启动器 + 后端运行时 + Flutter 前端资源），只搬一个 exe 会留下
    /// 旧后端 / 旧界面，所以这里按目录整体替换：
    ///   1. 安装目录里「新版没有」的条目 → 移到 backup（旧版本残留，如上一版的旧文件名）；
    ///   2. 与新版同名的条目 → 先移到 backup，再把新版放进来（覆盖正在被占用的文件会失败，
    ///      这种情况直接报错并还原，绝不留下半新半旧的目录）；
    ///   3. `.GC`、更新目录自身、正在运行的 GC-SAVEDOG.exe 一律跳过。
    ///
    /// 返回 false 时调用方应调用 <see cref="RestoreFromBackup"/> 还原。
    /// </summary>
    private bool ReplaceInstallDirectory(string stage, string installDir, string updateDir,
        List<(string Name, string? BackupPath)> replaced, out string? error)
    {
        error = null;
        var backupDir = Path.Combine(updateDir, "backup");

        try
        {
            Directory.CreateDirectory(installDir);
            ClearDirectory(backupDir);
            Directory.CreateDirectory(backupDir);

            var incoming = new List<string>();
            foreach (var entry in Directory.GetFileSystemEntries(stage))
            {
                var name = Path.GetFileName(entry);
                if (IsProtected(name))
                {
                    // 例如 GC-SAVEDOG.exe：本进程正在运行，Windows 上无法替换。
                    // 保留旧版守护进程即可，它下次启动会随包里的新版本一起被更新。
                    _log.Warn($"跳过受保护文件，保留旧版本：{name}",
                              $"Skipping protected file, keeping the old one: {name}");
                    continue;
                }
                incoming.Add(entry);
            }
            var incomingNames = new HashSet<string>(
                incoming.Select(e => Path.GetFileName(e)), StringComparer.OrdinalIgnoreCase);

            // 1. 旧版独有的条目先清走（保留在 backup 里以便回滚）
            foreach (var entry in Directory.GetFileSystemEntries(installDir))
            {
                var name = Path.GetFileName(entry);
                if (incomingNames.Contains(name)) continue;
                if (IsProtected(name)) continue;
                if (IsSameOrInside(entry, updateDir)) continue;

                var target = Path.Combine(backupDir, name);
                MoveEntry(entry, target);
                replaced.Add((name, target));
                _log.Info("旧版残留已备份: " + name, "Old leftover backed up: " + name);
            }

            // 2. 同名条目：旧 → backup，新 → 安装目录
            foreach (var entry in incoming)
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(installDir, name);
                var backup = Path.Combine(backupDir, name);

                if (File.Exists(target) || Directory.Exists(target))
                {
                    MoveEntry(target, backup);
                    replaced.Add((name, backup));
                }
                else
                {
                    // 纯新增：回滚时直接删掉，没有备份可还原
                    replaced.Add((name, null));
                }

                MoveEntry(entry, target);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 把备份里的条目原样搬回安装目录，撤掉新装的内容。
    /// <paramref name="BackupPath"/> 为 null 表示这一项是新包里独有的（没有旧版本），
    /// 回滚时只需要删掉。
    /// </summary>
    private bool RestoreFromBackup(string installDir, List<(string Name, string? BackupPath)> replaced)
    {
        var ok = true;
        foreach (var (name, backup) in replaced)
        {
            var target = Path.Combine(installDir, name);
            try
            {
                // 先撤掉新版（新包里独有的文件也要删干净，否则回滚后目录里新旧混杂）
                DeleteEntry(target);

                if (string.IsNullOrWhiteSpace(backup) ||
                    (!File.Exists(backup) && !Directory.Exists(backup)))
                    continue;

                MoveEntry(backup, target);
                _log.Info("已还原: " + name, "Restored: " + name);
            }
            catch (Exception ex)
            {
                ok = false;
                _log.Warn($"还原 {name} 失败: {ex.Message}", $"Failed to restore {name}: {ex.Message}");
            }
        }
        return ok;
    }

    /// <summary>更新成功：删掉整份备份，不再占用磁盘。</summary>
    private void CleanupBackup(string updateDir)
    {
        var backupDir = Path.Combine(updateDir, "backup");
        try
        {
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
        }
        catch (Exception ex)
        {
            _log.Warn("清理备份失败: " + ex.Message, "Failed to clean up the backup: " + ex.Message);
        }
    }

    /// <summary>
    /// 安装目录里不该被更新碰的东西：
    ///   · .GC —— 模型库、设置、日志、更新目录本身都在这里；
    ///   · GC-SAVEDOG.exe —— 本进程正在运行，Windows 上无法替换。
    /// </summary>
    private static bool IsProtected(string name)
    {
        if (string.Equals(name, ".GC", StringComparison.OrdinalIgnoreCase)) return true;
        var self = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(self) &&
            string.Equals(name, Path.GetFileName(self!), StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(name, "backup", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrInside(string path, string root)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return full.Equals(rootFull, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>移动条目；跨卷或目标占用时退回「复制 + 删除」。</summary>
    private static void MoveEntry(string from, string to)
    {
        var parent = Path.GetDirectoryName(to);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
            return;
        }

        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(from, to, overwrite: true);
            File.Delete(from);
        }
    }

    /// <summary>删除文件或目录，失败时抛出（由调用方决定是否回滚）。</summary>
    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }

    private bool TryLaunchPrevious(string installDir, string updateDir, LastGoodVersion? lastGood,
        string platformLabel)
    {
        // 优先用上次成功启动的主程序；否则按优先级在安装目录里找
        var candidates = new List<string>();
        if (lastGood != null && !string.IsNullOrWhiteSpace(lastGood.PackageName))
            candidates.Add(Path.Combine(installDir, lastGood.PackageName));

        foreach (var entry in Directory.GetFileSystemEntries(installDir))
        {
            var name = Path.GetFileName(entry);
            if (IsProtected(name)) continue;
            if (MainExecutablePriority(name) > 0) candidates.Add(entry);
            else if (Directory.Exists(entry) &&
                     name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                candidates.Add(entry);
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) continue;
            try
            {
                Launch(candidate);
                _log.Info("已回滚启动: " + Path.GetFileName(candidate),
                          "Rolled back and started: " + Path.GetFileName(candidate));
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn($"回滚启动失败 {Path.GetFileName(candidate)}: {ex.Message}",
                          $"Failed to start rollback candidate {Path.GetFileName(candidate)}: {ex.Message}");
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ 状态文件

    private void WriteState(string updateDir, SavedogState state)
    {
        try { Json.Write(Path.Combine(updateDir, SavedogFiles.State), state); }
        catch (Exception ex) { _log.Warn("写状态失败: " + ex.Message, "Failed to write state: " + ex.Message); }
    }

    private int Fail(string updateDir, string message, int code, string? messageEn = null)
    {
        _log.Error(message, messageEn ?? message);
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
