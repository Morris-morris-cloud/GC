using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>
/// 启动器更新走 GitCode Release，模型仍走仓库 contents API 的远端源。
///
/// · 版本判定：最新 Release 的 tag_name（如 v1.0.1 / GC-1.0.1）与本地程序集版本比较；
/// · 发布包：按附件名识别平台（如 GC-1.0.1-Win-amd64.zip），用 browser_download_url 下载；
/// · 校验：附件自带的 sha256/size（Gitee/GitCode 的 assets 可能没有 sha256，
///   此时退回 version.json 里同名 RID 条目的 sha256，两者都没有则只校验非空）；
/// · 模型清单与模型文件：完全委托给内部 GitCodeRemote，保持原有链路不变。
///
/// Release API 失败（仓库还没有 Release / 断网）时静默回退到 version.json 的
/// launcher 字段，保证「只发仓库、不发 Release」的旧发布方式仍然可用。
/// </summary>
public sealed class GitCodeReleaseRemote : IModelRemote
{
    private readonly GitCodeRemote _content;
    private readonly string _releasesUrl;
    private readonly Dictionary<string, (string Url, string? Sha256, long Size, string Name)> _assets = new();

    // 共享静态 HttpClient：避免每次检查更新都创建新实例导致端口耗尽；
    // headers 只在一处设置，多个线程并发读取是线程安全的。
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GC-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public GitCodeReleaseRemote(string? manifestUrl, string? repository)
    {
        _content = new GitCodeRemote(manifestUrl, repository);
        var (owner, repo) = ParseOwnerRepo(repository);
        _releasesUrl = $"https://api.gitcode.com/api/v5/repos/{owner}/{repo}/releases";
        DisplayName = (string.IsNullOrWhiteSpace(repository)
            ? GitCodeRemote.DefaultRepository : repository!.TrimEnd('/')) + " (Release)";
    }

    private static (string Owner, string Repo) ParseOwnerRepo(string? url)
    {
        var raw = string.IsNullOrWhiteSpace(url) ? GitCodeRemote.DefaultRepository : url!.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return ("Morris_1", "GC");
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return ("Morris_1", "GC");
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? parts[1][..^4] : parts[1];
        return (parts[0], repo);
    }

    public string DisplayName { get; }
    public string SourceId => SourcePreset.Detect(_content.DisplayName).Id;

    /// <summary>
    /// 模型部分照旧读仓库 version.json；成功拿到 Release 时，用 tag/附件覆盖启动器字段。
    /// </summary>
    /// <summary>
    /// 只认 Release：拿不到任何 Release 就返回 null，由上层去问另一个源。
    /// 仓库里的 version.json 只用来补全「模型清单」与平台包兜底地址，
    /// 不再用它充当版本号来源。
    /// </summary>
    public async Task<RepoVersion?> GetVersionAsync(CancellationToken ct)
    {
        var version = await _content.GetVersionAsync(ct);

        bool gotRelease;
        try
        {
            gotRelease = await ApplyReleaseAsync(version, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gitcode] 读取 Release 失败: {ex.Message}");
            gotRelease = false;
        }

        if (!gotRelease)
        {
            // 没有 Release：启动器版本留空（表示这个源没有可升级的启动器），
            // 但模型清单仍然有效，保证模型能正常同步。
            Console.WriteLine($"[gitcode] {DisplayName}: Release 为空（模型清单仍可用）");
            if (version != null)
            {
                version.Launcher.Version = "";
                version.Launcher.Packages.Clear();
            }
        }
        return version;
    }

    private async Task<bool> ApplyReleaseAsync(RepoVersion? version, CancellationToken ct)
    {
        if (version == null) return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var text = await Http.GetStringAsync(_releasesUrl, timeout.Token);
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '[') return false;   // 可能是错误页/对象

        using var doc = JsonDocument.Parse(text);
        ReleaseInfo? latest = null;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var info = ParseRelease(el);
            if (info == null || info.Draft || info.Prerelease) continue;
            // 取版本号最大的一条（API 通常已按时间倒序，这里再兜一层）
            if (latest == null || CompareVersion(info.Version, latest.Version) > 0) latest = info;
        }
        if (latest == null || string.IsNullOrWhiteSpace(latest.Version)) return false;

        version.Launcher.Version = latest.Version;

        _assets.Clear();
        foreach (var asset in latest.Assets)
        {
            var rid = asset.RidFromName(asset.Name);
            if (rid == null || string.IsNullOrWhiteSpace(asset.Url)) continue;
            // 同名平台取第一个有效附件
            if (!_assets.ContainsKey(rid))
                _assets[rid] = (asset.Url, asset.Sha256, asset.Size, asset.Name);
        }

        // 把附件地址写回 packages：有附件就用附件的直链与校验值，
        // 没有对应平台附件时保留 version.json 里原来的 path/url 兜底。
        foreach (var (rid, info) in _assets)
        {
            if (!version.Launcher.Packages.TryGetValue(rid, out var pkg))
            {
                pkg = new RepoPackage();
                version.Launcher.Packages[rid] = pkg;
            }
            pkg.Url = info.Url;
            pkg.AssetName = info.Name;
            if (!string.IsNullOrWhiteSpace(info.Sha256)) pkg.Sha256 = info.Sha256;
            if (info.Size > 0) pkg.Size = info.Size;
        }
        return true;
    }

    private static int CompareVersion(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return va.CompareTo(vb);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static ReleaseInfo? ParseRelease(JsonElement el)
    {
        try
        {
            var info = new ReleaseInfo
            {
                TagName = Str(el, "tag_name") ?? Str(el, "tag") ?? "",
                Name = Str(el, "name") ?? "",
                Draft = Bool(el, "draft") ?? false,
                Prerelease = Bool(el, "prerelease") ?? Bool(el, "pre_release") ?? false,
                PublishedAt = Str(el, "published_at") ?? Str(el, "created_at")
            };
            if (el.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    // 跳过 GitCode 自动生成的源码包（type=source），只取真正的发布附件
                    var type = Str(a, "type");
                    if (string.Equals(type, "source", StringComparison.OrdinalIgnoreCase)) continue;
                    info.Assets.Add(new ReleaseAsset
                    {
                        Name = Str(a, "name") ?? Str(a, "file_name") ?? "",
                        BrowserDownloadUrl = Str(a, "browser_download_url"),
                        DownloadUrl = Str(a, "download_url") ?? Str(a, "url"),
                        Size = Num(a, "size"),
                        Sha256 = Str(a, "sha256") ?? Str(a, "digest"),
                        Type = type
                    });
                }
            }
            // 有些平台把附件放在顶层 assets_link / attachments
            if (info.Assets.Count == 0 &&
                el.TryGetProperty("attachments", out var att) && att.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in att.EnumerateArray())
                    info.Assets.Add(new ReleaseAsset
                    {
                        Name = Str(a, "name") ?? "",
                        BrowserDownloadUrl = Str(a, "browser_download_url") ?? Str(a, "url"),
                        Size = Num(a, "size")
                    });
            }
            return info;
        }
        catch { return null; }
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool? Bool(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) &&
           (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;

    private static long Num(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt64(out var n) ? n : 0;

    // ---- 模型链路：原样委托，不做任何改动 ----
    public Task<IReadOnlyList<RepoEntry>> ListTreeAsync(string rootPath, CancellationToken ct)
        => _content.ListTreeAsync(rootPath, ct);

    public Task<byte[]> DownloadAsync(RepoEntry entry, CancellationToken ct)
        => _content.DownloadAsync(entry, ct);

    /// <summary>启动器包：优先用 Release 附件直链下载（url 已被 ApplyReleaseAsync 覆盖）。</summary>
    public Task<byte[]?> DownloadPackageAsync(RepoPackage package, CancellationToken ct)
        => _content.DownloadPackageAsync(package, ct);
}
