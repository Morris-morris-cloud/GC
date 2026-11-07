using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EarthModelStudio.Models;

namespace EarthModelStudio.Services;

/// <summary>
/// 启动时的自动更新：
///   · 读远端 version.json，和 .GC/settings.json 里记录的版本比较；
///   · 模型：下载到 cache 后原子替换 .GC/model/&lt;id&gt;，结果立刻刷新列表（本次启动生效）；
///   · 启动器：下载到 .GC/update/payload 并写 pending.json，下次启动才应用；
///   · 任何失败都保留本地已装模型，绝不把模型库清空。
/// </summary>
public sealed class UpdateService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly IModelRemote _remote;

    public UpdateService(AppPaths paths, SettingsService settings, IModelRemote remote)
    {
        _paths = paths;
        _settings = settings;
        _remote = remote;
    }

    public IModelRemote Remote => _remote;

    public static string CurrentLauncherVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            if (v == null) return "0.0.0";
            return $"{Math.Max(0, v.Major)}.{Math.Max(0, v.Minor)}.{Math.Max(0, v.Build)}";
        }
    }

    public static string CurrentRid
    {
        get
        {
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64"
            };
            if (OperatingSystem.IsWindows()) return "win-" + arch;
            if (OperatingSystem.IsMacOS()) return "osx-" + arch;
            if (OperatingSystem.IsLinux()) return "linux-" + arch;
            return RuntimeInformation.RuntimeIdentifier;
        }
    }

    public static IModelRemote CreateRemote(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.UpdateLocalRoot) &&
            Directory.Exists(settings.UpdateLocalRoot!))
            return new DirectoryRemote(settings.UpdateLocalRoot!);   // 离线 U 盘源：仍读本地 version.json
        // 在线源：启动器版本与发布包走 GitCode Release（tag + 附件），
        // 模型清单/文件仍走仓库 contents API；无 Release 时自动回退仓库 version.json。
        return new GitCodeReleaseRemote(settings.UpdateManifestUrl, settings.UpdateRepository);
    }

    public async Task<UpdateResult> RunAsync(bool force,
        IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        // 开发态的 model/ 里有源码与工程文件，自动更新一律跳过，避免把源码目录整体替换掉。
        // 需要验证更新链路请用 ./build.sh update-test（它使用临时目录，不碰仓库）。
        if (_paths.IsDevelopment && Environment.GetEnvironmentVariable("EMS_DEV_UPDATE") != "1")
            return new UpdateResult
            {
                Outcome = UpdateOutcome.Disabled,
                Message = "开发态跳过自动更新（保护仓库内模型源码）"
            };

        if (!force && !_settings.Current.AutoUpdateOnStartup)
            return new UpdateResult { Outcome = UpdateOutcome.Disabled, Message = L.I["update.disabled"] };

        progress?.Report(new UpdateProgress
        {
            Value = 0.02,
            Message = L.I["update.checking"],
            Detail = _remote.DisplayName
        });

        RepoVersion? remote;
        try
        {
            remote = await _remote.GetVersionAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new UpdateResult
            {
                Outcome = UpdateOutcome.Unavailable,
                Message = L.I["update.offline"],
                Error = ex.Message
            };
        }

        if (remote == null)
            return new UpdateResult
            {
                Outcome = UpdateOutcome.Unavailable,
                Message = L.I["update.noManifest"]
            };

        _settings.Current.LastUpdateCheck = DateTime.UtcNow.ToString("O");
        _settings.Save();

        var needed = remote.Models.Where(NeedsModel).ToList();
        var updated = new List<string>();
        var errors = new List<string>();

        for (var i = 0; i < needed.Count; i++)
        {
            var model = needed[i];
            progress?.Report(new UpdateProgress
            {
                Value = 0.05 + 0.72 * i / Math.Max(1, needed.Count),
                Message = L.I.F("update.downloadingModel", model.Id),
                Detail = model.Version
            });
            try
            {
                await InstallModelAsync(model, progress, i, needed.Count, ct);
                _settings.Current.InstalledModelVersions[model.Id] = model.Version;
                _settings.Save();
                updated.Add(model.Id);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{model.Id}: {ex.Message}");
                Console.Error.WriteLine($"模型更新失败 {model.Id}: {ex.Message}");
            }
        }

        string? launcherMessage = null;
        try
        {
            launcherMessage = await StageLauncherAsync(remote.Launcher, progress, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            launcherMessage = L.I.F("update.launcherFailed", ex.Message);
            errors.Add("launcher: " + ex.Message);
        }

        progress?.Report(new UpdateProgress { Value = 1, Message = L.I["update.done"] });

        var outcome = errors.Count > 0
            ? UpdateOutcome.Partial
            : (updated.Count > 0 || launcherMessage != null)
                ? UpdateOutcome.Applied
                : UpdateOutcome.NoChange;

        var message = updated.Count > 0
            ? L.I.F("update.applied", updated.Count)
            : launcherMessage != null ? L.I["update.modelsLatest"] : L.I["update.noChange"];
        if (errors.Count > 0) message += L.I.F("update.partial", errors.Count);

        return new UpdateResult
        {
            Outcome = outcome,
            Message = message,
            UpdatedModels = updated.Count,
            ModelNames = updated,
            LauncherMessage = launcherMessage,
            Error = errors.Count > 0 ? string.Join("; ", errors) : null
        };
    }

    private bool NeedsModel(RepoModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id) || string.IsNullOrWhiteSpace(model.Version)) return false;
        var dir = Path.Combine(_paths.ModelRoot, model.Id);
        if (!Directory.Exists(dir)) return true;
        if (!File.Exists(Path.Combine(dir, model.Id + ".json"))) return true;
        return !_settings.Current.InstalledModelVersions.TryGetValue(model.Id, out var local) ||
               !string.Equals(local, model.Version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>只下载模型清单与模型数据目录，源码 / 文档不进运行目录。</summary>
    private static bool IsPayload(string id, string relative)
    {
        var rel = relative.Replace('\\', '/');
        // 拒绝任何形式的父目录穿越与绝对路径
        if (rel.StartsWith('/') || rel.Contains("..", StringComparison.Ordinal)) return false;
        return rel.Equals(id + ".json", StringComparison.OrdinalIgnoreCase) ||
               rel.StartsWith(id + ".model/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallModelAsync(RepoModel model, IProgress<UpdateProgress>? progress,
        int index, int count, CancellationToken ct)
    {
        var rootPath = string.IsNullOrWhiteSpace(model.Dir) ? "model/" + model.Id : model.Dir;
        var entries = await _remote.ListTreeAsync(rootPath, ct);
        var files = entries.Where(e => !e.IsDirectory && IsPayload(model.Id, e.RelativePath)).ToList();

        if (!files.Any(e => e.RelativePath.Equals(model.Id + ".json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"远端缺少模型清单 {model.Id}.json");
        if (!files.Any(e => e.RelativePath.StartsWith(model.Id + ".model/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"远端缺少模型数据 {model.Id}.model/");

        var staging = Path.Combine(_paths.CacheRoot, "models", model.Id + "-" + Safe(model.Version));
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        try
        {
            var total = files.Sum(f => Math.Max(1L, f.Size));
            long done = 0;
            foreach (var entry in files)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await _remote.DownloadAsync(entry, ct);
                // 路径穿越防护：规范化后必须仍落在 staging 目录内，
                // 防止远端清单里的 "../" 或绝对路径写进安装目录之外。
                var dest = Path.GetFullPath(Path.Combine(staging,
                    entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                var stagingFull = Path.GetFullPath(staging)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"非法的模型文件路径: {entry.RelativePath}");
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                await File.WriteAllBytesAsync(dest, bytes, ct);
                done += bytes.Length;
                progress?.Report(new UpdateProgress
                {
                    Value = 0.05 + 0.72 * (index + Math.Min(1, (double)done / Math.Max(1, total))) / Math.Max(1, count),
                    Message = L.I.F("update.downloadingModel", model.Id),
                    Detail = entry.RelativePath
                });
            }

            // 原子替换：先备份旧目录，成功后再删备份；失败自动回滚
            Directory.CreateDirectory(_paths.ModelRoot);
            var target = Path.Combine(_paths.ModelRoot, model.Id);
            // 双保险：任何情况下都不允许把含 src/ 源码的模型目录整体换掉
            if (Directory.Exists(Path.Combine(target, "src")))
                throw new InvalidOperationException($"目标目录含模型源码 src/，拒绝覆盖: {target}");
            var backup = target + ".old";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target);
                throw;
            }
            if (Directory.Exists(backup))
            {
                try { Directory.Delete(backup, true); } catch { /* 占用时留到下次 */ }
            }
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            throw;
        }
    }

    private async Task<string?> StageLauncherAsync(RepoLauncher launcher,
        IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        var current = CurrentLauncherVersion;
        var pending = _settings.Current.PendingLauncherVersion;

        if (!string.IsNullOrWhiteSpace(pending) &&
            string.Equals(pending, launcher.Version, StringComparison.OrdinalIgnoreCase))
            return L.I.F("update.pendingReady", launcher.Version);

        if (!IsNewer(launcher.Version, current)) return null;

        var rid = CurrentRid;
        if (!launcher.Packages.TryGetValue(rid, out var package) &&
            !launcher.Packages.TryGetValue("default", out package))
            return L.I.F("update.noRidPackage", launcher.Version, rid);

        progress?.Report(new UpdateProgress
        {
            Value = 0.85,
            Message = L.I.F("update.downloadingLauncher", launcher.Version)
        });

        var bytes = await _remote.DownloadPackageAsync(package!, ct);
        if (bytes == null || bytes.Length == 0)
            return L.I.F("update.noPackageUrl", launcher.Version);

        if (!string.IsNullOrWhiteSpace(package!.Sha256))
        {
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, package.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("启动器包校验失败（sha256 不符）");
        }

        Directory.CreateDirectory(_paths.UpdateRoot);
        var payload = Path.Combine(_paths.UpdateRoot, "payload");
        if (Directory.Exists(payload)) Directory.Delete(payload, true);
        Directory.CreateDirectory(payload);

        // 单文件发布包（.exe / .app.zip 里的可执行文件不在此列）就是程序本体，不能解压。
        // 直接按附件原名放进 payload，脚本会把 payload/ 整体覆盖到安装目录。
        var assetName = package!.AssetName;
        bool singleFile = !string.IsNullOrWhiteSpace(assetName) &&
                          !assetName!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                          !assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        string executableName;
        if (singleFile)
        {
            executableName = Path.GetFileName(assetName!);
            await File.WriteAllBytesAsync(Path.Combine(payload, executableName), bytes, ct);
        }
        else
        {
            var zip = Path.Combine(_paths.CacheRoot, "launcher-" + Safe(launcher.Version) + ".zip");
            await File.WriteAllBytesAsync(zip, bytes, ct);
            try { ZipSafety.ExtractToDirectory(zip, payload); }
            finally { try { File.Delete(zip); } catch { } }
            FlattenPayload(payload);
            executableName = Path.GetFileName(Environment.ProcessPath ?? "");
        }

        Json.Write(Path.Combine(_paths.UpdateRoot, "pending.json"), new PendingUpdate
        {
            Version = launcher.Version,
            Rid = rid,
            InstallDirectory = _paths.InstallDirectory,
            Executable = executableName,
            Payload = "payload",
            DownloadedAt = DateTime.UtcNow.ToString("O")
        });

        _settings.Current.PendingLauncherVersion = launcher.Version;
        _settings.Save();
        return L.I.F("update.pendingDownloaded", launcher.Version);
    }

    private static void FlattenPayload(string dir)
    {
        var entries = Directory.GetFileSystemEntries(dir);
        if (entries.Length != 1 || !Directory.Exists(entries[0])) return;
        var inner = entries[0];
        var flat = dir + "-flat";
        if (Directory.Exists(flat)) Directory.Delete(flat, true);
        Directory.Move(inner, flat);
        Directory.Delete(dir, true);
        Directory.Move(flat, dir);
    }

    private static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l)) return r > l;
        return !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase);
    }

    private static string Safe(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Replace('/', '-').Replace(' ', '-');
    }
}
