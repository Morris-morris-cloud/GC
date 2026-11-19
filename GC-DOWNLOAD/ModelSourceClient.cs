using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GC.Models;

namespace GC.Services;

/// <summary>
/// 从链接导入模型源。
/// 支持：git 仓库地址（GitHub / GitLab / Gitee / 任意 git 地址）、zip 直链、本地目录。
/// 仓库里的 model/ 目录就是模型列表；下载后缓存在应用缓存目录。
/// </summary>
public sealed class ModelSourceClient : IModelSourceClient
{
    private readonly AppPaths _paths;
    private static readonly HttpClient Http = CreateHttp();

    public ModelSourceClient(AppPaths paths) => _paths = paths;

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GC/1.0 (+model-source)");
        return client;
    }

    public async Task<SourceIndex> FetchAsync(ModelSource source, bool force, IProgress<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source.Url))
            return new SourceIndex { SourceId = source.Id, Error = "dl.error.emptyUrl" };

        try
        {
            var (root, fromCache) = await ResolveRootAsync(source, force, log, ct);
            var models = new List<RemoteModel>();
            foreach (var (manifest, dir) in ModelScanner.Scan(Path.Combine(root, "model")))
            {
                models.Add(new RemoteModel
                {
                    Manifest = manifest,
                    Directory = dir,
                    SizeBytes = ModelScanner.DirectorySize(dir),
                    SourceId = source.Id
                });
            }

            return new SourceIndex
            {
                SourceId = source.Id,
                Root = root,
                Models = models,
                FromCache = fromCache,
                ResolvedFrom = root
            };
        }
        catch (OperationCanceledException)
        {
            return new SourceIndex { SourceId = source.Id, Error = "dl.error.canceled" };
        }
        catch (Exception ex)
        {
            return new SourceIndex { SourceId = source.Id, Error = ex.Message };
        }
    }

    private async Task<(string Root, bool FromCache)> ResolveRootAsync(ModelSource source, bool force,
        IProgress<string>? log, CancellationToken ct)
    {
        var raw = source.Url.Trim();

        // 1) 本地目录（开发模型源最方便）
        var localPath = raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(raw).LocalPath
            : raw;
        if (Directory.Exists(localPath))
        {
            log?.Report("local");
            return (localPath, true);
        }

        var cacheDir = Path.Combine(_paths.CacheRoot, "sources", source.Id);
        if (!force && Directory.Exists(Path.Combine(cacheDir, "model")))
        {
            log?.Report("cache");
            return (cacheDir, true);
        }

        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        Directory.CreateDirectory(cacheDir);

        // 2) zip 直链 / 托管平台归档
        foreach (var url in ArchiveCandidates(raw, source.Branch))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                log?.Report("zip:" + url);
                await DownloadZipAsync(url, cacheDir, ct);
                if (Directory.Exists(Path.Combine(cacheDir, "model")))
                    return (cacheDir, false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log?.Report("zip-failed:" + ex.Message);
            }
        }

        // 3) git clone（需要本机安装 git）
        log?.Report("git");
        if (await TryGitCloneAsync(raw, cacheDir, source.Branch, ct))
            return (cacheDir, false);

        throw new InvalidOperationException("dl.error.unreachable");
    }

    private static IEnumerable<string> ArchiveCandidates(string url, string? branch)
    {
        if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            yield return url;
            yield break;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) yield break;
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) yield break;

        var owner = segments[0];
        var repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        var host = uri.Host.ToLowerInvariant();

        var branches = new List<string>();
        if (!string.IsNullOrWhiteSpace(branch)) branches.Add(branch!);
        else if (host.Contains("gitee")) { branches.Add("master"); branches.Add("main"); }
        else { branches.Add("main"); branches.Add("master"); }

        foreach (var b in branches)
        {
            if (host.Contains("github.com"))
                yield return $"https://codeload.github.com/{owner}/{repo}/zip/refs/heads/{b}";
            else if (host.Contains("gitlab"))
                yield return $"https://gitlab.com/{owner}/{repo}/-/archive/{b}/{repo}-{b}.zip";
            else if (host.Contains("gitee.com"))
                yield return $"https://gitee.com/{owner}/{repo}/repository/archive/{b}.zip";
            else
            {
                // 自托管 Gitea（如 git.xhmcl.com）：用 archive/<branch>.zip 拉源码归档。
                // 关键：不能在这里 yield break —— 那样就只剩 git clone 一条路，
                // 用户机器没装 git 时「检测模型 / 下载 / 自动更新」会全部失效。
                // 归档下载走 ResumableDownloader，无需系统 git。
                yield return $"{uri.GetLeftPart(UriPartial.Authority)}/{owner}/{repo}/archive/{b}.zip";
            }
        }
    }

    private async Task DownloadZipAsync(string url, string cacheDir, CancellationToken ct)
    {
        // .part 用 URL 哈希做稳定文件名：同一个源重试时命中同一份半成品，
        // 断点续传才有意义（用 GUID 的话每次重试都是新文件，等于从零开始）。
        // 解压目录仍用 GUID —— 它只是中间产物，成功后整块移走或失败即删。
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
        var partPath = Path.Combine(_paths.CacheRoot, "tmp", "downloads", key + ".part");
        var tmpRoot = Path.Combine(_paths.CacheRoot, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpRoot);
        var zipPath = Path.Combine(tmpRoot, "source.zip");
        try
        {
            // 带断点续传的流式下载；网络中断时 .part 保留，下次从这里接上。
            await ResumableDownloader.DownloadAsync(Http, url, partPath, null, ct)
                .ConfigureAwait(false);
            // 下载完整，.part 转正。转正成功即代表这份 zip 已完整，删掉 .part 避免重复下载。
            File.Move(partPath, zipPath, overwrite: true);

            var extractDir = Path.Combine(tmpRoot, "extract");
            Directory.CreateDirectory(extractDir);
            ZipSafety.ExtractToDirectory(zipPath, extractDir);

            var root = FindModelRoot(extractDir) ?? extractDir;
            foreach (var entry in Directory.GetFileSystemEntries(root))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(cacheDir, name);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                else if (File.Exists(dest)) File.Delete(dest);
                if (Directory.Exists(entry)) Directory.Move(entry, dest);
                else File.Move(entry, dest);
            }
        }
        finally
        {
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
        }
    }

    private static string? FindModelRoot(string dir, int depth = 0)
    {
        if (Directory.Exists(Path.Combine(dir, "model"))) return dir;
        if (depth >= 2) return null;
        foreach (var child in Directory.GetDirectories(dir))
        {
            var found = FindModelRoot(child, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static async Task<bool> TryGitCloneAsync(string url, string cacheDir, string? branch, CancellationToken ct)
    {
        if (cacheDir.Length > 0 && Directory.Exists(cacheDir))
        {
            foreach (var f in Directory.GetFileSystemEntries(cacheDir))
            {
                if (Directory.Exists(f)) Directory.Delete(f, true);
                else File.Delete(f);
            }
        }

        // 只拉取 model/ 目录：整仓 clone 会把 .git / src / dist 一起拖下来
        // （实测 700MB+），既慢又容易超时，表现就是「读不到模型源」。
        // 先浅克隆到临时目录，再用 sparse-checkout 只检出 model/。
        var tmpDir = cacheDir + ".tmp";
        try
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);

            // 1) 初始化 + 加远程（比 clone 更快，避免先拉全量）
            var initArgs = new List<string> { "init", "--quiet", tmpDir };
            if (!await RunGitAsync(initArgs, ct)) return false;

            var remoteArgs = new List<string> { "-C", tmpDir, "remote", "add", "origin", url };
            if (!await RunGitAsync(remoteArgs, ct)) return false;

            // 2) 只允许 model 目录参与检出
            await RunGitAsync(new List<string> { "-C", tmpDir, "config", "core.sparseCheckout", "true" }, ct);
            var sparseFile = Path.Combine(tmpDir, ".git", "info", "sparse-checkout");
            Directory.CreateDirectory(Path.GetDirectoryName(sparseFile)!);
            File.WriteAllText(sparseFile, "model/\n");

            // 3) 浅拉指定分支
            var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch!;
            var fetchArgs = new List<string>
            {
                "-C", tmpDir, "fetch", "--depth", "1", "--filter=blob:none", "origin", branchName
            };
            if (!await RunGitAsync(fetchArgs, ct)) return false;

            await RunGitAsync(new List<string> { "-C", tmpDir, "checkout", "FETCH_HEAD" }, ct);

            // 4) 把 model/ 移到缓存根，与原有目录结构一致
            var modelDir = Path.Combine(tmpDir, "model");
            if (!Directory.Exists(modelDir))
            {
                Console.Error.WriteLine("git sparse-checkout 后未找到 model/ 目录");
                return false;
            }

            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
            Directory.CreateDirectory(cacheDir);
            Directory.Move(modelDir, Path.Combine(cacheDir, "model"));
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git 拉取异常: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
        }
    }

    /// <summary>安静地跑一条 git 命令，返回是否成功。</summary>
    private static async Task<bool> RunGitAsync(List<string> args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process == null) return false;
            var errTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var err = await errTask;
            if (process.ExitCode != 0)
                Console.Error.WriteLine($"git {string.Join(' ', args)} 失败: {err.Trim()}");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git 执行失败: {ex.Message}");
            return false;
        }
    }
}
