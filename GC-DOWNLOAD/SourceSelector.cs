using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>
/// 双源选择：Xhmcl 与 GitCode 是同一份仓库的两个镜像。
///
/// 启动更新 / 下载页面 / 检查更新时，先并发探测两边的 version.json 响应时间，
/// 取**最快且内容可用**的那个作为本次下载源；探测结果缓存一段时间，
/// 避免每次点下载都重新测速。
/// </summary>
public sealed class SourceSelector : ISourceSelector
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("GC-Probe/1.0");
        return c;
    }

    private readonly object _lock = new();
    private SourcePreset? _cached;
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(3);

    /// <summary>最近一次选中的源（未探测时为空）。</summary>
    public SourcePreset? Current { get { lock (_lock) return _cached; } }

    /// <summary>每个源的探测耗时（毫秒），供界面展示。</summary>
    public IReadOnlyDictionary<string, long> LastLatency { get; private set; } =
        new Dictionary<string, long>();

    /// <summary>
    /// 探测两个源并返回最快的。force=true 时忽略缓存。
    /// 两个都失败时返回 GitCode（默认源）。
    /// </summary>
    public async Task<SourcePreset> PickFastestAsync(bool force = false, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!force && _cached != null && DateTime.UtcNow - _cachedAt < _ttl)
                return _cached;
        }

        var probes = SourcePreset.All.Select(async p =>
        {
            var sw = Stopwatch.StartNew();
            var ok = await ProbeAsync(p, ct);
            sw.Stop();
            return (Preset: p, Ok: ok, Ms: sw.ElapsedMilliseconds);
        });

        var results = await Task.WhenAll(probes);

        var latencies = results.ToDictionary(r => r.Preset.Id, r => r.Ms);
        LastLatency = latencies;

        var usable = results.Where(r => r.Ok).OrderBy(r => r.Ms).ToList();
        var chosen = usable.Count > 0 ? usable[0].Preset : SourcePreset.GitCode;

        lock (_lock)
        {
            _cached = chosen;
            _cachedAt = DateTime.UtcNow;
        }

        Console.WriteLine("[selector] 测速：" + string.Join("  ",
            results.Select(r => $"{r.Preset.Name}={r.Ms}ms{(r.Ok ? "" : "(不可用)")}"))
            + $"  → 选择 {chosen.Name}");

        return chosen;
    }

    /// <summary>
    /// 探测：能拿到合法 JSON 才算可用。
    /// 注意两个平台取文件的方式不同：
    ///   · Gitea(Xhmcl)：raw/branch/{b}/{path} 直接返回文件内容
    ///   · GitCode：raw 地址返回的是 HTML 页面，必须走 API（内容为 base64）
    /// 因此 GitCode 用 api/v5 contents + Accept: application/json 探测。
    /// </summary>
    private static async Task<bool> ProbeAsync(SourcePreset preset, CancellationToken ct)
    {
        if (preset.Kind == "gitcode")
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var api = $"{preset.ApiBase}/repos/{preset.Owner}/{preset.Repo}/contents/version.json?ref={preset.Branch}";
                using var req = new HttpRequestMessage(HttpMethod.Get, api);
                req.Headers.Accept.ParseAdd("application/json");
                using var resp = await Http.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode) return false;
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                return text.TrimStart().StartsWith('{');
            }
            catch { return false; }
        }

        var urls = new[] { preset.RawUrl("version.json"), preset.RawUrl("model/version.json") };

        foreach (var url in urls)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var text = await Http.GetStringAsync(url, cts.Token);
                if (text.TrimStart().StartsWith('{')) return true;
            }
            catch { /* 换下一个地址 */ }
        }
        return false;
    }

    /// <summary>为指定源创建对应的 remote 实现。</summary>
    public static IModelRemote CreateRemote(SourcePreset preset) => preset.Kind switch
    {
        "gitea" => new GiteaRemote(preset),
        _ => new GitCodeReleaseRemote(null, preset.HomeUrl)
    };
}
