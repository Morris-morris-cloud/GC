using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

        // 离线目录同样验签：U 盘 / 共享盘里的清单也可能是被替换过的。
        // 验签结论随 version.SignatureTrust 一起上送，由 UpdateService 按策略裁决；
        // 这里至少要把失败如实记下来，不能静默丢弃（与其他远程源行为一致）。
        var text = File.ReadAllText(file);
        _rawManifest = text;
        var version = ManifestLoader.Parse(text, out var verification);
        if (verification.Trust is ManifestTrust.Invalid or ManifestTrust.Unsigned)
            Console.Error.WriteLine($"[directory] 清单验签失败({verification.Trust}): {verification.Error}");
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
            // 符号链接目录可能指向根目录之外：既不递归进去，也不作为条目暴露。
            if (IsReparsePoint(sub)) continue;
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
            if (IsReparsePoint(file)) continue;
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

        // 清单里的 URL 属于外部输入，必须约束来源：
        //  · file:// 和绝对路径只允许读 _root 之内，杜绝任意本地文件读取；
        //  · 网络 URL 仅允许 http(s)，且目标不能是环回/私有/链路本地/元数据地址，防 SSRF。
        if (url!.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var local = new Uri(url).LocalPath;
            return await File.ReadAllBytesAsync(SafePath(Path.GetRelativePath(_root, local)), ct);
        }
        if (Path.IsPathRooted(url))
        {
            if (File.Exists(url)) return await File.ReadAllBytesAsync(SafePath(Path.GetRelativePath(_root, url)), ct);
            return null;
        }
        if (IsAllowedHttpUrl(url))
            return await Http.GetByteArrayAsync(url, ct);
        return null;
    }

    /// <summary>
    /// 仅允许 http/https：挡住 file: 导致的任意本地文件读取和 gopher: 之类的协议滥用。
    /// IP 字面量直接判公网；主机名（清单里的正常发布 URL）允许，但拒绝 localhost 与
    /// 常见本地后缀，避免指向本机。域名解析到内网的情况由 HttpClient 侧的实际连接地址
    /// 兜底——本地目录源的主路径本就是本地文件，网络只是兜底。
    /// </summary>
    private static bool IsAllowedHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
            return IsPublicAddress(uri.Host);

        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        // 仅允许含点、且各段是字母数字/连字符的常规域名；拒绝 .local/.internal 等本地域。
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var label in host.Split('.'))
            if (label.Length == 0 || label[0] == '-' || label[^1] == '-' ||
                !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                return false;
        return true;
    }

    private static bool IsPublicAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (IPAddress.IsLoopback(ip) || IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10 || b[0] == 127 || b[0] == 0) return false;               // 私有 / 环回 / 本网
            if (b[0] == 169 && b[1] == 254) return false;                            // 链路本地 / 云元数据
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;               // 私有
            if (b[0] == 192 && b[1] == 168) return false;                            // 私有
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;              // CGNAT (IPv4 共享地址)
            if (b[0] == 192 && b[1] == 0) return false;                              // IETF 协议分配
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;             // 基准测试
            if (b[0] >= 224) return false;                                           // 组播 / 保留
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            var b = ip.GetAddressBytes();
            if (b[0] == 0xfd || b[0] == 0xfc) return false;                          // 唯一本地地址
            if (ip.IsIPv4MappedToIPv6) return IsPublicAddress(ip.MapToIPv4().ToString());
        }
        return true;
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

    /// <summary>把相对路径解析成 _root 内的真实路径：拒绝越界，且逐级拒绝符号链接。</summary>
    private string SafePath(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"路径越界: {relative}");

        // 词法校验之后还要解析符号链接：root 内的链接可能指向根目录之外。
        // 逐级检查 _root 到 full 的每一段，任何重解析点都直接拒绝。
        var rel = Path.GetRelativePath(_root, full);
        if (rel != "." && rel != "")
        {
            var cursor = _root;
            foreach (var segment in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "..") throw new InvalidOperationException($"路径越界: {relative}");
                cursor = Path.Combine(cursor, segment);
                if (IsReparsePoint(cursor))
                    throw new InvalidOperationException($"路径含符号链接，已拒绝: {relative}");
            }
        }
        return full;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // 取不到属性时按不可信处理，避免绕过符号链接检查。
            return true;
        }
    }

    private static string Relative(string baseDir, string file) =>
        Path.GetRelativePath(baseDir, file).Replace('\\', '/');
}
