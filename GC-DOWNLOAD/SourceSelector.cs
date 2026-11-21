using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>
/// 多源选择：Xhmcl 与 GitHub 是同一份仓库的两个镜像。
///
/// 启动更新 / 下载页面 / 检查更新时，先并发探测各源的 version.json 响应时间，
/// 取**最快且内容可用**的那个作为本次下载源；探测结果缓存一段时间，
/// 避免每次点下载都重新测速。单个源探测超时 2 秒，慢源不会拖住整体。
/// </summary>
public sealed class SourceSelector : ISourceSelector
{
    /// <summary>单源探测超时：超过 2 秒视为不可用（用户可见的「ping 2s」）。</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    // 使用共享 HttpClient 工厂，统一连接管理，避免端口耗尽
    private static readonly HttpClient Http = SharedHttpClientFactory.GetDefault();

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
    /// 两个都失败时退回主源（Xhmcl）：GitCode 镜像已停更，用它兜底只会拿到过期的
    /// version.json —— 反而让人以为「没有更新」。
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
        // 全部探测失败时退回主源（SourcePreset.All 的第一项 = Xhmcl），
        // 不要退回已停更的 GitCode 镜像。
        var fallback = SourcePreset.All.Length > 0 ? SourcePreset.All[0] : SourcePreset.Xhmcl;
        var chosen = usable.Count > 0 ? usable[0].Preset : fallback;

        if (usable.Count == 0)
            GcLog.Warn($"所有源探测失败，退回主源 {chosen.Name}",
                       $"All probes failed, fallback to {chosen.Name}");

        lock (_lock)
        {
            _cached = chosen;
            // 全部探测失败的结果不缓存：网络刚恢复时应该立刻重新测速，
            // 而不是被一次失败的探测锁住 3 分钟。
            _cachedAt = usable.Count > 0 ? DateTime.UtcNow : DateTime.MinValue;
        }

        GcLog.Info($"测速结果：{string.Join("  ",
            results.Select(r => $"{r.Preset.Name}={r.Ms}ms{(r.Ok ? "" : "(不可用)")}"))}  → 选择 {chosen.Name}",
            $"Probe results: {string.Join(", ",
            results.Select(r => $"{r.Preset.Name}={r.Ms}ms{(r.Ok ? "" : "(unavailable)")}"))}  -> chosen: {chosen.Name}");

        return chosen;
    }

    /// <summary>
    /// 探测：能拿到合法 JSON 才算可用。
    /// 注意各平台取文件的方式不同：
    ///   · Gitea(Xhmcl)：raw/branch/{b}/{path} 直接返回文件内容
    ///   · GitHub：raw.githubusercontent.com/{b}/{path} 直接返回文件内容
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
                cts.CancelAfter(ProbeTimeout);
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
                cts.CancelAfter(ProbeTimeout);
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
        // GitHub 的 contents / releases API 与 Gitea 同构，复用同一实现
        "github" => new GiteaRemote(preset),
        "gitcode" => new GitCodeReleaseRemote(null, preset.HomeUrl),
        _ => throw new InvalidOperationException($"未知的源类型: {preset.Kind}")
    };
}
