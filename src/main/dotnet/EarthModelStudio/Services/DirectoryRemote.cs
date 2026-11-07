using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EarthModelStudio.Services;

/// <summary>
/// 本地目录源：用于离线教学（U 盘 / 共享盘里的仓库副本）与自动化测试。
/// 目录结构与远端仓库一致：version.json + model/&lt;id&gt;/...
/// </summary>
public sealed class DirectoryRemote : IModelRemote
{
    private readonly string _root;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public DirectoryRemote(string root) => _root = Path.GetFullPath(root);

    public string DisplayName => _root;

    public Task<RepoVersion?> GetVersionAsync(CancellationToken ct)
    {
        var primary = SafePath("version.json");
        var fallback = SafePath(Path.Combine("model", "version.json"));
        var file = File.Exists(primary) ? primary : fallback;
        if (!File.Exists(file)) return Task.FromResult<RepoVersion?>(null);
        return Task.FromResult(Json.Read<RepoVersion>(file));
    }

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
        if (!string.IsNullOrWhiteSpace(package.Path))
        {
            var local = SafePath(package.Path!);
            if (File.Exists(local)) return await File.ReadAllBytesAsync(local, ct);
        }
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
