using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EarthModelStudio.Models;

namespace EarthModelStudio.Services;

/// <summary>
/// 从链接导入模型源。
/// 支持：git 仓库地址（GitHub / GitLab / Gitee / 任意 git 地址）、zip 直链、本地目录。
/// 仓库里的 model/ 目录就是模型列表；下载后缓存到 cache/sources/&lt;id&gt;。
/// </summary>
public sealed class ModelSourceClient
{
    private readonly AppPaths _paths;
    private static readonly HttpClient Http = CreateHttp();

    public ModelSourceClient(AppPaths paths) => _paths = paths;

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EarthModelStudio/1.0 (+model-source)");
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
                yield break;
        }
    }

    private async Task DownloadZipAsync(string url, string cacheDir, CancellationToken ct)
    {
        var tmpRoot = Path.Combine(_paths.CacheRoot, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpRoot);
        var zipPath = Path.Combine(tmpRoot, "source.zip");
        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zipPath);
                await input.CopyToAsync(output, 81920, ct);
            }

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

        var args = new List<string> { "clone", "--depth", "1" };
        if (!string.IsNullOrWhiteSpace(branch)) { args.Add("--branch"); args.Add(branch!); }
        args.Add(url);
        args.Add(cacheDir);

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
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"git clone 失败: {stderr}");
                return false;
            }
            return Directory.Exists(Path.Combine(cacheDir, "model")) || Directory.Exists(cacheDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git clone 异常: {ex.Message}");
            return false;
        }
    }
}
