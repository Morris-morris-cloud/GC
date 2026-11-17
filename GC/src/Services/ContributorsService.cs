using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>仓库的一位贡献者（按人归并后的结果）。</summary>
public sealed class Contributor
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    /// <summary>提交次数。</summary>
    public int Contributions { get; set; }

    /// <summary>接口直接返回的头像地址（Gitea 的 avatar_url）；为空时按邮箱哈希生成。</summary>
    public string? Avatar { get; set; }

    /// <summary>最近一次提交时间，用于贡献数相同时排序。</summary>
    public DateTimeOffset? LastCommit { get; set; }

    /// <summary>关于页展示的名字。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "匿名" : Name;

    /// <summary>头像地址：优先用接口返回的头像，否则按邮箱哈希拼 GitCode 头像。</summary>
    public string? AvatarUrl =>
        !string.IsNullOrWhiteSpace(Avatar)
            ? Avatar
            : string.IsNullOrWhiteSpace(Email)
                ? null
                : $"https://cdn-img.gitcode.com/avatar/{Convert.ToHexString(
                    System.Security.Cryptography.MD5.HashData(
                        System.Text.Encoding.UTF8.GetBytes(Email!.Trim().ToLowerInvariant())))
                    .ToLowerInvariant()}";
}

/// <summary>
/// 读取仓库的真实贡献者：翻页拉提交记录，按作者归并统计。
///
/// 为什么不用 /collaborators：Gitea 的协同者列表要求登录，且只包含被显式
/// 添加的协作者，和「谁真的写了代码」是两回事。这里直接读 /commits，
/// 统计每个人真实的提交数，失败时静默返回空列表，不影响关于页其它内容。
/// </summary>
public static class ContributorsService
{
    private const int PageSize = 50;
    private const int MaxPages = 12;
    private const int MaxShown = 8;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly HttpClient Http = CreateHttp();

    /// <summary>同一作者的多个 git 身份 -> 统一展示名。</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["morris"] = "Morris",
        ["morris_1"] = "Morris",
        ["2509_92447106"] = "Morris",
        ["chenmoyuan1214@qq.com"] = "Morris",
        ["morris@xhmcl.com"] = "Morris",
    };

    /// <summary>自动化提交（CI / 助手账号），不计入开发者。</summary>
    private static readonly HashSet<string> IgnoredEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "agent@teamily.ai",
    };

    private static readonly object Gate = new();
    private static (DateTimeOffset At, string Key, List<Contributor> Items)? _cache;

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("GC/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    /// <summary>按仓库地址推导接口：Xhmcl(Gitea) 与 GitCode 结构不同。</summary>
    public static string BuildUrl(string repository)
    {
        var raw = string.IsNullOrWhiteSpace(repository)
            ? SourcePreset.Xhmcl.HomeUrl : repository.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return "https://git.xhmcl.com/api/v1/repos/Morris/GC/commits";

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "https://git.xhmcl.com/api/v1/repos/Morris/GC/commits";

        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? parts[1][..^4] : parts[1];

        // GitCode 的实现在仓库仍指向 gitcode.com 时才走它。
        if (uri.Host.Contains("gitcode.com", StringComparison.OrdinalIgnoreCase))
            return $"https://api.gitcode.com/api/v5/repos/{parts[0]}/{repo}/contributors";

        return $"https://{uri.Host}/api/v1/repos/{parts[0]}/{repo}/commits";
    }

    public static async Task<List<Contributor>> FetchAsync(string repository, CancellationToken ct = default)
    {
        var url = BuildUrl(repository);
        lock (Gate)
        {
            if (_cache is { } hit && hit.Key == url && DateTimeOffset.UtcNow - hit.At < CacheTtl)
                return hit.Items;
        }

        List<Contributor> list;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            list = url.Contains("api.gitcode.com", StringComparison.OrdinalIgnoreCase)
                ? await FetchFromGitCodeAsync(url, timeout.Token)
                : await FetchFromCommitsAsync(url, timeout.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"读取贡献者失败: {ex.Message}");
            return new List<Contributor>();
        }

        lock (Gate) _cache = (DateTimeOffset.UtcNow, url, list);
        return list;
    }

    /// <summary>Gitea：翻页读提交记录并归并作者。</summary>
    private static async Task<List<Contributor>> FetchFromCommitsAsync(string commitUrl, CancellationToken ct)
    {
        var byKey = new Dictionary<string, Contributor>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"{commitUrl}?limit={PageSize}&page={page}&stat=false";
            var text = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) break;

            var seen = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                seen++;

                string? login = null, name = null, email = null, avatar = null;
                DateTimeOffset? date = null;

                if (item.TryGetProperty("author", out var author) &&
                    author.ValueKind == JsonValueKind.Object)
                {
                    login = author.TryGetProperty("login", out var l) ? l.GetString() : null;
                    avatar = author.TryGetProperty("avatar_url", out var a) ? a.GetString() : null;
                }

                if (item.TryGetProperty("commit", out var commit) &&
                    commit.TryGetProperty("author", out var ca) &&
                    ca.ValueKind == JsonValueKind.Object)
                {
                    name = ca.TryGetProperty("name", out var n) ? n.GetString() : null;
                    email = ca.TryGetProperty("email", out var e) ? e.GetString() : email;
                    if (ca.TryGetProperty("date", out var d) &&
                        DateTimeOffset.TryParse(d.GetString(), out var parsed))
                        date = parsed;
                }

                Add(byKey, login, name, email, avatar, date);
            }

            if (seen < PageSize) break;
        }

        return Sort(byKey.Values);
    }

    /// <summary>GitCode：contributors 接口直接返回贡献数与邮箱。</summary>
    private static async Task<List<Contributor>> FetchFromGitCodeAsync(string url, CancellationToken ct)
    {
        var text = await Http.GetStringAsync(url, ct);
        if (text.TrimStart().Length == 0 || text.TrimStart()[0] != '[')
            return new List<Contributor>();

        var byKey = new Dictionary<string, Contributor>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(text);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var email = item.TryGetProperty("email", out var e) ? e.GetString() : null;
            var count = item.TryGetProperty("contributions", out var c) && c.TryGetInt32(out var v) ? v : 1;
            var contributor = Add(byKey, null, name, email, null, null, count);
            if (contributor != null) contributor.Contributions = Math.Max(contributor.Contributions, count);
        }

        return Sort(byKey.Values);
    }

    private static Contributor? Add(
        Dictionary<string, Contributor> byKey,
        string? login, string? name, string? email, string? avatar, DateTimeOffset? date,
        int weight = 1)
    {
        if (!string.IsNullOrWhiteSpace(email) && IgnoredEmails.Contains(email.Trim()))
            return null;

        var (key, display) = Resolve(login, name, email);
        if (!byKey.TryGetValue(key, out var contributor))
        {
            contributor = new Contributor { Name = display };
            byKey[key] = contributor;
        }

        contributor.Contributions += weight;
        if (string.IsNullOrWhiteSpace(contributor.Email) && !string.IsNullOrWhiteSpace(email))
            contributor.Email = email;
        if (string.IsNullOrWhiteSpace(contributor.Avatar) && !string.IsNullOrWhiteSpace(avatar))
            contributor.Avatar = avatar;
        if (date.HasValue && (contributor.LastCommit == null || date > contributor.LastCommit))
            contributor.LastCommit = date;

        return contributor;
    }

    /// <summary>把同一个人在不同时期的 git 身份归并到同一个展示名。</summary>
    private static (string Key, string Display) Resolve(string? login, string? name, string? email)
    {
        foreach (var raw in new[] { login, name, email })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (Aliases.TryGetValue(raw.Trim(), out var display))
                return ("#" + display.ToLowerInvariant(), display);
        }

        var label = !string.IsNullOrWhiteSpace(login) ? login!
            : !string.IsNullOrWhiteSpace(name) ? name!
            : email!;
        return (label.Trim().ToLowerInvariant(), label.Trim());
    }

    private static List<Contributor> Sort(IEnumerable<Contributor> items) =>
        items
            .OrderByDescending(c => c.Contributions)
            .ThenByDescending(c => c.LastCommit ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxShown)
            .ToList();
}
