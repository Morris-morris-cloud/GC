using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EarthModelStudio.Services;

/// <summary>GitCode 仓库的一位贡献者。</summary>
public sealed class Contributor
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    /// <summary>提交次数（API 里的 contributions）。</summary>
    public int Contributions { get; set; }

    /// <summary>
    /// 登录名 -> 展示名。GitCode 的 contributors 接口只返回账号登录名，
    /// 不返回真实姓名，这里做一层映射，让关于页显示作者想展示的名字。
    /// </summary>
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2509_92447106"] = "Morris"
    };

    /// <summary>头像地址：GitCode 用邮箱哈希拼头像，取不到时返回 null。</summary>
    public string? AvatarUrl =>
        string.IsNullOrWhiteSpace(Email)
            ? null
            : $"https://cdn-img.gitcode.com/avatar/{Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Email!.Trim().ToLowerInvariant())))
                .ToLowerInvariant()}";

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "匿名";
            return DisplayNames.TryGetValue(Name.Trim(), out var mapped) ? mapped : Name;
        }
    }
}

/// <summary>
/// 读取 GitCode 仓库的贡献者列表（用于「关于」页面展示开发者）。
/// 失败时静默返回空列表，不影响关于页其它内容。
/// </summary>
public static class ContributorsService
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EarthModelStudio/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    /// <summary>默认仓库的 contributors 接口地址。</summary>
    public static string BuildUrl(string repository)
    {
        var raw = string.IsNullOrWhiteSpace(repository)
            ? GitCodeRemote.DefaultRepository : repository.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return "https://api.gitcode.com/api/v5/repos/2509_92447106/GC/contributors";

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "https://api.gitcode.com/api/v5/repos/2509_92447106/GC/contributors";

        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? parts[1][..^4] : parts[1];
        return $"https://api.gitcode.com/api/v5/repos/{parts[0]}/{repo}/contributors";
    }

    public static async Task<List<Contributor>> FetchAsync(string repository, CancellationToken ct = default)
    {
        var url = BuildUrl(repository);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var text = await Http.GetStringAsync(url, timeout.Token);
            var trimmed = text.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '[') return new List<Contributor>();

            var list = JsonSerializer.Deserialize<List<Contributor>>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<Contributor>();

            // 按提交数从多到少，贡献最多的排最前
            list.Sort((a, b) => b.Contributions.CompareTo(a.Contributions));
            return list;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"读取贡献者失败: {ex.Message}");
            return new List<Contributor>();
        }
    }
}
