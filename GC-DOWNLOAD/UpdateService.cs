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
using GC.Models;

namespace GC.Services;

/// <summary>
/// 模型自动更新：读远端版本清单，把缺失/过期的模型下载到 cache 后原子替换
/// .GC/model/&lt;id&gt;，本次启动立即生效。任何失败都保留本地已装模型。
/// 启动器更新由 LauncherUpdater / LauncherUpdateFlow 独立负责，不在这里。
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private IModelRemote _remote;

    public UpdateService(AppPaths paths, SettingsService settings, IModelRemote remote)
    {
        _paths = paths;
        _settings = settings;
        _remote = remote;
    }

    public IModelRemote Remote => _remote;

    /// <summary>换源：测速选出最快的一个后，用它的 remote 覆盖当前实例。</summary>
    public void UseRemote(IModelRemote remote) => _remote = remote;

    /// <summary>
    /// 同步版本（无法测速时用）：按设置里的仓库地址映射到内置源。
    /// 真正带测速的入口是 <see cref="CreateRemoteAsync"/>。
    /// </summary>
    public static IModelRemote CreateRemote(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.UpdateLocalRoot) &&
            Directory.Exists(settings.UpdateLocalRoot!))
            return new DirectoryRemote(settings.UpdateLocalRoot!);
        return SourceSelector.CreateRemote(SourcePreset.Detect(settings.UpdateRepository));
    }

    /// <summary>
    /// 异步版本：先探测 Xhmcl / GitCode 两个源，取最快的那个。
    /// 启动更新、下载页、检查更新统一走这里，保证「用同一个源、同一种下载方式」。
    /// </summary>
    public static async Task<IModelRemote> CreateRemoteAsync(AppSettings settings,
        SourceSelector selector, bool forceProbe = false, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(settings.UpdateLocalRoot) &&
            Directory.Exists(settings.UpdateLocalRoot!))
            return new DirectoryRemote(settings.UpdateLocalRoot!);

        var preset = await selector.PickFastestAsync(forceProbe, ct);
        return SourceSelector.CreateRemote(preset);
    }

    public async Task<UpdateResult> RunAsync(bool force,
        IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        // 开发态的 model/ 里有源码与工程文件，模型自动替换会破坏源码目录，
        // 因此开发态只做「启动器版本检查」（下载到 .GC/update 暂存，可验证链路），
        // 跳过模型替换。要用正式流程验证请跑 ./build.sh update-test。
        var devMode = _paths.IsDevelopment &&
                      Environment.GetEnvironmentVariable("EMS_DEV_UPDATE") != "1";

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

        // 启动器更新由 LauncherUpdater / LauncherUpdateFlow 独立负责，
        // 这里只做「模型同步到最新版」。
        string? launcherMessage = null;

        // ---- 同步所有模型到最新版 ----
        // 开发态跳过：仓库 model/ 里有源码，整体替换会破坏工程目录。
        var needed = devMode
            ? new List<RepoModel>()
            : remote.Models.Where(NeedsModel).ToList();
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

        progress?.Report(new UpdateProgress { Value = 1, Message = L.I["update.done"] });

        var outcome = errors.Count > 0
            ? UpdateOutcome.Partial
            : (updated.Count > 0 || launcherMessage != null)
                ? UpdateOutcome.Applied
                : UpdateOutcome.NoChange;

        var message = updated.Count > 0
            ? L.I.F("update.applied", updated.Count)
            : launcherMessage != null
                ? L.I["update.modelsLatest"]
                : devMode
                    ? "开发态：已检查启动器，模型替换已跳过（保护仓库源码）"
                    : L.I["update.noChange"];
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
            // 并行下载：模型包通常有几十个文件，串行下载会明显拖慢启动。
            // 并发上限 6，避免被服务端限流；进度用原子计数汇总。
            long done = 0;
            var stagingFull = Path.GetFullPath(staging)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using var gate = new SemaphoreSlim(6);

            var tasks = files.Select(async entry =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var bytes = await _remote.DownloadAsync(entry, ct);

                    // 路径穿越防护
                    var dest = Path.GetFullPath(Path.Combine(staging,
                        entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"非法的模型文件路径: {entry.RelativePath}");

                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    await File.WriteAllBytesAsync(dest, bytes, ct);

                    var now = Interlocked.Add(ref done, bytes.Length);
                    progress?.Report(new UpdateProgress
                    {
                        Value = 0.05 + 0.72 * (index + Math.Min(1, (double)now / Math.Max(1, total))) / Math.Max(1, count),
                        Message = L.I.F("update.downloadingModel", model.Id),
                        Detail = entry.RelativePath
                    });
                }
                finally { gate.Release(); }
            });

            await Task.WhenAll(tasks);

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

    private static string Safe(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Replace('/', '-').Replace(' ', '-');
    }
}
