using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>
/// gitcode 仓库源。默认地址来自 AppSettings.UpdateRepository
/// （https://gitcode.com/Morris_1/GC），可改成任意 gitcode 仓库或
/// https://gitcode.com/&lt;owner&gt;/&lt;repo&gt;/tree/&lt;branch&gt;/model 这样的地址。
/// 匿名访问 v5 contents API：目录 -> 数组，文件 -> { content: base64 }；
/// 文件项自带 download_url（raw.gitcode.com/blobs/&lt;sha&gt;/...），可直接下载二进制。
/// </summary>
public sealed class GitCodeRemote : IModelRemote
{
    public const string DefaultRepository = "https://gitcode.com/Morris_1/GC";

    private readonly HttpClient _http;
    private readonly string _api;
    private readonly string _branch;
    private readonly string _versionUrl;
    private readonly string? _fallbackUrl;
    private readonly string _displayName;
    private string _modelDir = "model";
    private string? _rawManifest;
    /// <summary>最近一次读到的启动器版本号，用于推导 dist 目录里的包名。</summary>
    private string _launcherVersion = "";

    public string DisplayName => _displayName;

    public GitCodeRemote(string? manifestUrl = null, string? repository = null)
    {
        var (owner, repo, branch) = ParseRepository(repository);
        _branch = branch;
        _api = $"https://api.gitcode.com/api/v5/repos/{owner}/{repo}/contents/";
        _displayName = string.IsNullOrWhiteSpace(repository)
            ? DefaultRepository : repository!.TrimEnd('/');

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GC-Updater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            _versionUrl = _api + "version.json?ref=" + _branch;
            _fallbackUrl = _api + "model/version.json?ref=" + _branch;
        }
        else
        {
            _versionUrl = manifestUrl!;
            _fallbackUrl = null;
        }
    }

    /// <summary>从仓库 URL 解析 owner / repo / branch，解析失败回退到默认仓库。</summary>
    private static (string Owner, string Repo, string Branch) ParseRepository(string? url)
    {
        var raw = string.IsNullOrWhiteSpace(url) ? DefaultRepository : url!.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return ("Morris_1", "GC", "main");
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return ("Morris_1", "GC", "main");
        var owner = parts[0];
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? parts[1][..^4] : parts[1];
        var branch = parts.Length >= 4 && parts[2].Equals("tree", StringComparison.OrdinalIgnoreCase)
            ? parts[3] : "main";
        return (owner, repo, branch);
    }

    public async Task<RepoVersion?> GetVersionAsync(CancellationToken ct)
    {
        RepoVersion? version = await ReadVersionFromAsync(_versionUrl, ct);
        if (version == null && _fallbackUrl != null)
            version = await ReadVersionFromAsync(_fallbackUrl, ct);
        if (version != null && !string.IsNullOrWhiteSpace(version.ModelDir))
            _modelDir = version.ModelDir.Trim('/');
        if (version != null && !string.IsNullOrWhiteSpace(version.Launcher.Version))
            _launcherVersion = version.Launcher.Version.Trim();
        return version;
    }

    /// <summary>最近一次读到的清单原文（SAVEDOG 二次验签用）。</summary>
    public Task<string?> GetRawVersionTextAsync(CancellationToken ct)
        => Task.FromResult(_rawManifest);

    /// <summary>
    /// 由 Release 覆盖版本号后同步一次，保证 dist 目录里的包名推导用的是
    /// 「真正要装的那个版本」，而不是仓库清单里可能滞后的版本号。
    /// </summary>
    internal void SetLauncherVersion(string? version)
    {
        if (!string.IsNullOrWhiteSpace(version)) _launcherVersion = version!.Trim();
    }

    private async Task<RepoVersion?> ReadVersionFromAsync(string url, CancellationToken ct)
    {
        string text;
        try
        {
            // 离线 / 网络不通时最多等 10 秒，不能让启动界面一直卡在「检查更新」
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            text = await _http.GetStringAsync(url, timeout.Token);
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var payload = text;
            if (doc.RootElement.TryGetProperty("encoding", out _) &&
                doc.RootElement.TryGetProperty("content", out var content))
            {
                var raw = Convert.FromBase64String(content.GetString() ?? "");
                payload = Encoding.UTF8.GetString(raw);
            }
            if (!payload.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;

            // 验签与解析一起做：拿到的清单必须能说明「它是不是可信的」
            var version = ManifestLoader.Parse(payload, out var verification);
            if (version == null) return null;

            // 404 / 错误页也可能返回一个 JSON 对象，用 generated 确认这确实是版本清单
            if (string.IsNullOrWhiteSpace(version.Generated)) return null;

            if (verification.Trust == ManifestTrust.Invalid)
            {
                Console.Error.WriteLine($"[gitcode] 清单验签失败，已拒绝: {verification.Error}");
                return null;
            }

            if (version != null && !string.IsNullOrWhiteSpace(version.Generated))
                _rawManifest = payload;
            return version;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"版本清单解析失败 {url}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 递归列出目录树（含目录项与文件项）。
    /// 每个子目录一次 HTTP 请求，因此并行展开同一层的所有目录，
    /// 否则 3 个模型、十几个目录串行请求会非常慢。
    /// </summary>
    public async Task<IReadOnlyList<RepoEntry>> ListTreeAsync(string rootPath, CancellationToken ct)
    {
        var result = new List<RepoEntry>();
        var bag = new System.Collections.Concurrent.ConcurrentBag<RepoEntry>();
        var repoPath = (rootPath ?? _modelDir).Trim('/');
        await WalkParallelAsync(repoPath, "", 0, bag, ct);

        // 稳定排序：目录在前、按路径字典序，保证调用方拿到确定顺序
        result = bag.OrderBy(e => e.IsDirectory ? 0 : 1)
                    .ThenBy(e => e.RelativePath, StringComparer.Ordinal)
                    .ToList();
        return result;
    }

    private async Task WalkParallelAsync(string repoPath, string relative, int depth,
        System.Collections.Concurrent.ConcurrentBag<RepoEntry> bag, CancellationToken ct)
    {
        if (depth > 8) return;
        var url = _api + repoPath + "?ref=" + _branch;
        string text;
        try { text = await GetWithRetryAsync(url, ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"列目录失败 {repoPath}: {ex.Message}");
            return;
        }
        if (!text.TrimStart().StartsWith("[", StringComparison.Ordinal)) return;

        var subDirs = new List<(string Path, string Relative)>();

        using (var doc = JsonDocument.Parse(text))
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = Str(item, "name");
                if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
                // 这些目录不是模型运行数据，跳过可显著减少请求
                // （模型目录里常见的 src/ bin/ obj/ 是源码与构建产物）
                if (IsIgnoredDir(name)) continue;
                var path = Str(item, "path");
                if (string.IsNullOrWhiteSpace(path)) path = repoPath + "/" + name;
                var isDir = Str(item, "type") == "dir";
                var childRelative = relative.Length == 0 ? name : relative + "/" + name;

                bag.Add(new RepoEntry
                {
                    Name = name,
                    Path = path!,
                    RelativePath = childRelative,
                    IsDirectory = isDir,
                    DownloadUrl = NullableStr(item, "download_url"),
                    Sha = NullableStr(item, "sha"),
                    Size = Num(item, "size")
                });

                if (isDir) subDirs.Add((path!, childRelative));
            }
        }

        if (subDirs.Count == 0) return;

        // 同层并行；并发保持很低——GitCode API 对突发请求会返回 429
        using var gate = new SemaphoreSlim(2);
        var tasks = subDirs.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try { await WalkParallelAsync(d.Path, d.Relative, depth + 1, bag, ct); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task WalkAsync(string repoPath, string relative, int depth,
        List<RepoEntry> result, CancellationToken ct)
    {
        if (depth > 8) return;
        var url = _api + repoPath + "?ref=" + _branch;
        var text = await _http.GetStringAsync(url, ct);
        if (!text.TrimStart().StartsWith("[", StringComparison.Ordinal)) return;

        using var doc = JsonDocument.Parse(text);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var name = Str(item, "name");
            if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
            var path = Str(item, "path");
            if (string.IsNullOrWhiteSpace(path)) path = repoPath + "/" + name;
            var isDir = Str(item, "type") == "dir";
            var childRelative = relative.Length == 0 ? name : relative + "/" + name;

            result.Add(new RepoEntry
            {
                Name = name,
                Path = path!,
                RelativePath = childRelative,
                IsDirectory = isDir,
                DownloadUrl = NullableStr(item, "download_url"),
                Sha = NullableStr(item, "sha"),
                Size = Num(item, "size")
            });

            if (isDir) await WalkAsync(path!, childRelative, depth + 1, result, ct);
        }
    }

    /// <summary>遍历时忽略的目录：源码与构建产物不属于模型运行数据。</summary>
    private static bool IsIgnoredDir(string name) => name switch
    {
        "src" => true,
        "bin" => true,
        "obj" => true,
        "node_modules" => true,
        ".git" => true,
        ".vs" => true,
        _ => false
    };

    /// <summary>
    /// 带退避重试的 GET。GitCode API 对突发请求返回 429，
    /// 这里做 3 次指数退避，避免并行遍历时直接失败。
    /// </summary>
    private async Task<string> GetWithRetryAsync(string url, CancellationToken ct,
        int maxAttempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _http.GetStringAsync(url, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                // 429 / 5xx 才重试，其余直接抛
                var retriable = ex.StatusCode is null ||
                                (int)ex.StatusCode == 429 ||
                                (int)ex.StatusCode >= 500;
                if (!retriable) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt * attempt), ct);
            }
        }
    }

    public async Task<byte[]> DownloadAsync(RepoEntry entry, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entry.DownloadUrl))
            return await _http.GetByteArrayAsync(entry.DownloadUrl!, ct);

        var url = _api + entry.Path.Trim('/') + "?ref=" + _branch;
        var text = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("content", out var content))
            throw new InvalidOperationException($"远端文件没有内容: {entry.Path}");
        return Convert.FromBase64String(content.GetString() ?? "");
    }

    public async Task<byte[]?> DownloadPackageAsync(RepoPackage package, CancellationToken ct)
    {
        // 先试清单直给的 url（Release 附件）：命中的情况下不多发任何一次 API 请求。
        var direct = await TryFetchAsync(package.Url, package.Sha256, ct);
        if (direct != null) return direct;

        // url 不可用或校验不符：再去找仓库里的编译产物。
        // 顺序：清单 path → dist/<系统>-<架构>/ 里的 zip → dist 根目录命名。
        // 每个候选都要和已签名清单的 sha256 对一遍，对不上就换下一个，
        // 因此「多来源」不放宽校验，只是让「不发 Release 也能更新」成立。
        foreach (var path in LauncherArtifacts.CandidateFiles(
                     package.Rid, package.Dir, package.Path, package.Name, _launcherVersion))
        {
            var url = await ResolveFileUrlAsync(path, ct);
            var bytes = await TryFetchAsync(url, package.Sha256, ct);
            if (bytes != null) return bytes;
        }

        foreach (var dir in LauncherArtifacts.CandidateDirs(package.Rid, package.Dir))
        {
            var url = await FindZipInDirAsync(dir, package, ct);
            var bytes = await TryFetchAsync(url, package.Sha256, ct);
            if (bytes != null) return bytes;
        }

        Console.Error.WriteLine("[gitcode] 找不到可用的启动器包（Release 附件与 dist 目录都没对上）");
        return null;
    }

    /// <summary>下载一个候选地址；<paramref name="sha256"/> 非空时必须与内容一致。</summary>
    private async Task<byte[]?> TryFetchAsync(string? url, string? sha256, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        byte[] bytes;
        try { bytes = await _http.GetByteArrayAsync(url!, ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gitcode] 下载失败 {Short(url!)}: {ex.Message}");
            return null;
        }
        if (bytes.Length == 0) return null;

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[gitcode] 包校验不符，换下一个来源: {Short(url!)}");
                return null;
            }
        }

        Console.WriteLine($"[gitcode] 启动器包来源: {Short(url!)}");
        return bytes;
    }

    private static string Short(string url)
        => url.Length <= 96 ? url : url[..60] + "…" + url[^24..];

    /// <summary>把仓库内路径解析成直链；路径不存在或其实是目录时返回 null。</summary>
    private async Task<string?> ResolveFileUrlAsync(string path, CancellationToken ct)
    {
        try
        {
            var url = _api + path.Trim('/') + "?ref=" + _branch;
            var text = await _http.GetStringAsync(url, ct);
            if (!text.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "dir", StringComparison.OrdinalIgnoreCase))
                return null;
            return doc.RootElement.TryGetProperty("download_url", out var dl)
                ? dl.GetString() : null;
        }
        catch (Exception)
        {
            return null;   // 404 / 网络问题都当作「这个路径没有」
        }
    }

    /// <summary>
    /// 在 <paramref name="dir"/> 里找启动器包。
    /// 先按「版本 + 平台」精确筛，其次接受目录里唯一的 zip（发布者可能改了命名规则），
    /// 最终仍由 sha256 兜底。
    /// </summary>
    private async Task<string?> FindZipInDirAsync(string dir, RepoPackage package, CancellationToken ct)
    {
        try
        {
            var url = _api + dir.Trim('/') + "?ref=" + _branch;
            var text = await _http.GetStringAsync(url, ct);
            if (!text.TrimStart().StartsWith("[", StringComparison.Ordinal)) return null;

            var zips = new List<(string Path, string Name)>();
            using (var doc = JsonDocument.Parse(text))
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (Str(item, "type") != "file") continue;
                    var name = Str(item, "name");
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    var path = Str(item, "path");
                    zips.Add((path.Length > 0 ? path : dir + "/" + name, name));
                }
            }
            if (zips.Count == 0) return null;

            var version = _launcherVersion;
            var match = zips.FindIndex(z =>
                LauncherArtifacts.LooksLikePackage(z.Name, version, package.Rid));
            if (match < 0)
            {
                if (zips.Count != 1) return null;
                match = 0;
            }

            Console.WriteLine($"[gitcode] 在 {dir} 找到编译产物: {zips[match].Name}");
            return await ResolveFileUrlAsync(zips[match].Path, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Str(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string? NullableStr(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static long Num(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt64(out var n) ? n : 0;
}
