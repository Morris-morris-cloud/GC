using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>
/// 本地目录源：用于离线教学（U 盘 / 共享盘里的仓库副本）与自动化测试。
/// 目录结构与远端仓库一致：version.json + model/&lt;id&gt;/...
/// </summary>
public sealed class DirectoryRemote : IModelRemote
{
    private readonly string _root;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private string? _rawManifest;
    private string _launcherVersion = "";

    public DirectoryRemote(string root) => _root = Path.GetFullPath(root);

    public string DisplayName => _root;

    public Task<RepoVersion?> GetVersionAsync(CancellationToken ct)
    {
        var primary = SafePath("version.json");
        var fallback = SafePath(Path.Combine("model", "version.json"));
        var file = File.Exists(primary) ? primary : fallback;
        if (!File.Exists(file)) return Task.FromResult<RepoVersion?>(null);

        // 离线目录同样验签：U 盘 / 共享盘里的清单也可能是被替换过的
        var text = File.ReadAllText(file);
        _rawManifest = text;
        var version = ManifestLoader.Parse(text, out _);
        if (version != null && !string.IsNullOrWhiteSpace(version.Launcher.Version))
            _launcherVersion = version.Launcher.Version.Trim();
        return Task.FromResult(version);
    }

    /// <summary>最近一次读到的清单原文（SAVEDOG 二次验签用）。</summary>
    public Task<string?> GetRawVersionTextAsync(CancellationToken ct)
        => Task.FromResult(_rawManifest);

    public Task<IReadOnlyList<RepoEntry>> ListTreeAsync(string rootPath, CancellationToken ct)
    {
        var baseDir = SafePath(rootPath ?? "model");
        var result = new List<RepoEntry>();
        if (!Directory.Exists(baseDir)) return Task.FromResult<IReadOnlyList<RepoEntry>>(result);

        Walk(baseDir, baseDir, 0, result);
        return Task.FromResult<IReadOnlyList<RepoEntry>>(result);
    }

    private void Walk(string dir, string baseDir, int depth, List<RepoEntry> result)
    {
        if (depth > 8) return;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.')) continue;
            var rel = Relative(baseDir, sub);
            result.Add(new RepoEntry
            {
                Name = name,
                Path = Relative(_root, sub),
                RelativePath = rel,
                IsDirectory = true
            });
            Walk(sub, baseDir, depth + 1, result);
        }
        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            result.Add(new RepoEntry
            {
                Name = name,
                Path = Relative(_root, file),
                RelativePath = Relative(baseDir, file),
                IsDirectory = false,
                Size = new FileInfo(file).Length
            });
        }
    }

    public async Task<byte[]> DownloadAsync(RepoEntry entry, CancellationToken ct)
        => await File.ReadAllBytesAsync(SafePath(entry.Path), ct);

    public async Task<byte[]?> DownloadPackageAsync(RepoPackage package, CancellationToken ct)
    {
        // 清单路径 / dist 根目录命名：按仓库内相对路径直接找
        foreach (var rel in LauncherArtifacts.CandidateFiles(
                     package.Rid, package.Dir, package.Path, package.Name, _launcherVersion))
        {
            string full;
            try { full = SafePath(rel); }
            catch (InvalidOperationException) { continue; }
            if (File.Exists(full)) return await File.ReadAllBytesAsync(full, ct);
        }

        // dist/<系统>-<架构>/ 里放着的编译产物（与远端同一套查找规则）：
        // 本地源同样要能跑通「只发产物、不发 Release」的更新方式。
        var fromDist = FindInDist(package);
        if (fromDist != null) return await File.ReadAllBytesAsync(fromDist, ct);

        var url = package.Url;
        if (string.IsNullOrWhiteSpace(url)) return null;
        // 离线 U 盘源与 ./build.sh update-test 都会用本地 zip 路径，必须保持兼容。
        if (url!.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return await File.ReadAllBytesAsync(new Uri(url).LocalPath, ct);
        if (Path.IsPathRooted(url) && File.Exists(url))
            return await File.ReadAllBytesAsync(url, ct);
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return await Http.GetByteArrayAsync(url, ct);
        return null;
    }

    /// <summary>在 dist/&lt;系统&gt;-&lt;架构&gt;/ 里找这个平台的启动器包；找不到返回 null。</summary>
    private string? FindInDist(RepoPackage package)
    {
        foreach (var dir in LauncherArtifacts.CandidateDirs(package.Rid, package.Dir))
        {
            string full;
            try { full = SafePath(dir); }
            catch (InvalidOperationException) { continue; }
            if (!Directory.Exists(full)) continue;

            var zips = Directory.GetFiles(full, "*.zip");
            if (zips.Length == 0) continue;

            foreach (var zip in zips)
                if (LauncherArtifacts.LooksLikePackage(Path.GetFileName(zip), _launcherVersion, package.Rid))
                    return zip;
            if (zips.Length == 1) return zips[0];
        }
        return null;
    }

    private string SafePath(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"路径越界: {relative}");
        return full;
    }

    private static string Relative(string baseDir, string file) =>
        Path.GetRelativePath(baseDir, file).Replace('\\', '/');
}
