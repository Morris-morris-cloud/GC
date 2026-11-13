using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EarthModelStudio.Services;

/// <summary>
/// Gitea / Forgejo 后端（Xhmcl 用的就是这个）。
///
/// · 目录：GET /api/v1/repos/{owner}/{repo}/contents/{path}
///        返回数组（目录）或对象（单文件），带 name/path/size/type/download_url
/// · 原始文件：GET {host}/{owner}/{repo}/raw/branch/{branch}/{path}
/// · Release：GET /api/v1/repos/{owner}/{repo}/releases
///        版本取 tag_name；附件取 assets[].browser_download_url
///
/// Release 为空时（仓库还没发布），回退到仓库里的 version.json，保证只推仓库也能更新。
/// </summary>
public sealed class GiteaRemote : IModelRemote
{
    private readonly SourcePreset _preset;
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EarthModelStudio-Updater/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    public GiteaRemote(SourcePreset preset)
    {
        _preset = preset;
        DisplayName = preset.Name;
    }

    public string DisplayName { get; }
    public string SourceId => _preset.Id;

    private string RepoApi => $"{_preset.ApiBase}/repos/{_preset.Owner}/{_preset.Repo}";

    // ---------------------------------------------------------------- 清单

    /// <summary>
    /// 两件事分开取：
    ///   · **模型清单**永远来自仓库 version.json（模型不需要发 Release）
    ///   · **启动器版本**只认 Release；没有 Release 时把版本号留空，
    ///     由上层去问另一个源（LeaderUpdater 会横向比较）。
    /// 以前把两者绑在一起，导致没有 Release 时连模型都同步不了。
    /// </summary>
    public async Task<RepoVersion?> GetVersionAsync(CancellationToken ct)
    {
        var fromRepo = await TryGetVersionFromRepoAsync(ct);
        var fromRelease = await TryGetVersionFromReleasesAsync(ct);

        // 模型清单：仓库里有就用仓库的（保证模型能更新）
        var result = fromRepo ?? new RepoVersion
        {
            Repository = _preset.HomeUrl,
            Branch = _preset.Branch
        };

        if (fromRelease != null)
        {
            // Release 提供启动器版本与平台附件
            result.Launcher.Version = fromRelease.Launcher.Version;
            foreach (var kv in fromRelease.Launcher.Packages)
                result.Launcher.Packages[kv.Key] = kv.Value;
            return result;
        }

        // 没有 Release：清空启动器版本，让上层知道"这个源没有可升级的启动器"
        Console.WriteLine($"[gitea] {_preset.Name}: Release 为空（模型清单仍可用）");
        result.Launcher.Version = "";
        result.Launcher.Packages.Clear();

        // 只有模型清单时也返回，保证模型能同步；启动器版本为空表示无升级
        return result.Models.Count > 0 ? result : null;
    }
    /// <summary>读仓库里的 version.json（模型清单 + 兜底信息）。</summary>
    private async Task<RepoVersion?> TryGetVersionFromRepoAsync(CancellationToken ct)
    {
        foreach (var url in new[] { _preset.RawUrl("version.json"), _preset.RawUrl("model/version.json") })
        {
            try
            {
                var text = await Http.GetStringAsync(url, ct);
                if (!text.TrimStart().StartsWith('{')) continue;   // 可能返回 HTML
                var doc = JsonSerializer.Deserialize<RepoVersion>(text, Json.Options);
                if (doc != null) return doc;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[gitea] version.json 读取失败 {url}: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 从 Release 列表里挑**版本号最高**的一个（不是最新发布时间的那个），
    /// 并把它对应平台的附件填进 packages。
    /// </summary>
    private async Task<RepoVersion?> TryGetVersionFromReleasesAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync($"{RepoApi}/releases", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var candidates = new List<(string Version, JsonElement Node)>();
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;   // 草稿不算发布
                var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                var ver = VersionUtil.Normalize(tag);
                if (string.IsNullOrWhiteSpace(ver)) continue;
                candidates.Add((ver, rel));
            }
            if (candidates.Count == 0)
            {
                // 还没发布 Release 是正常状态：交给上层去问另一个源
                Console.WriteLine($"[gitea] {_preset.Name}: Release 列表为空");
                return null;
            }

            var best = candidates
                .OrderByDescending(c => VersionUtil.Parse(c.Version))
                .First();

            var result = new RepoVersion
            {
                Repository = _preset.HomeUrl,
                Branch = _preset.Branch,
                Launcher = new RepoLauncher { Version = best.Version }
            };

            // 附件：按文件名识别平台
            if (best.Node.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;

                    var rid = PlatformFromAssetName(name);
                    if (rid == null) continue;

                    var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                    result.Launcher.Packages[rid] = new RepoPackage
                    {
                        Url = url,
                        AssetName = name,
                        Size = size
                    };
                }
            }

            return result.Launcher.Packages.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gitea] Release 读取失败（{_preset.Name}）: {ex.Message}");
            return null;
        }
    }

    /// <summary>从附件名推断 RID：GC-1.0.6-win-amd64.zip → win-x64</summary>
    private static string? PlatformFromAssetName(string name)
    {
        var n = name.ToLowerInvariant();
        if (!n.EndsWith(".zip")) return null;
        if (n.Contains("win") && n.Contains("arm64")) return "win-arm64";
        if (n.Contains("win") && (n.Contains("amd64") || n.Contains("x64"))) return "win-x64";
        if (n.Contains("mac") && n.Contains("arm64")) return "osx-arm64";
        if (n.Contains("mac") && n.Contains("x64")) return "osx-x64";
        if (n.Contains("linux") && n.Contains("x64")) return "linux-x64";
        return null;
    }

    // ---------------------------------------------------------------- 目录

    public async Task<IReadOnlyList<RepoEntry>> ListTreeAsync(string rootPath, CancellationToken ct)
    {
        var result = new List<RepoEntry>();
        await WalkAsync(rootPath.Trim('/'), "", result, ct, depth: 0);
        return result;
    }

    private async Task WalkAsync(string apiPath, string relPrefix, List<RepoEntry> sink,
        CancellationToken ct, int depth)
    {
        if (depth > 8) return;

        var url = $"{RepoRepoContentsUrl()}/{apiPath}";
        string json;
        try { json = await Http.GetStringAsync(url, ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gitea] 列目录失败 {apiPath}: {ex.Message}");
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 单文件
        if (root.ValueKind == JsonValueKind.Object)
        {
            AddFile(root, relPrefix, sink);
            return;
        }
        if (root.ValueKind != JsonValueKind.Array) return;

        var subdirs = new List<(string Path, string Rel)>();
        foreach (var item in root.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "";
            var name = item.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(name)) continue;
            // 跳过构建产物与源码，模型包只需要清单与数据
            if (type == "dir")
            {
                if (IsIgnoredDir(name)) continue;
                subdirs.Add((apiPath + "/" + name, relPrefix + name + "/"));
            }
            else
            {
                AddFile(item, relPrefix, sink);
            }
        }

        foreach (var (p, rel) in subdirs)
            await WalkAsync(p, rel, sink, ct, depth + 1);
    }

    private string RepoRepoContentsUrl() => $"{RepoApi}/contents";

    private static void AddFile(JsonElement item, string relPrefix, List<RepoEntry> sink)
    {
        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : name;
        var size = item.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
        var dl = item.TryGetProperty("download_url", out var d) ? d.GetString() : null;
        var sha = item.TryGetProperty("sha", out var sh) ? sh.GetString() : null;

        sink.Add(new RepoEntry
        {
            Name = name,
            Path = path,
            RelativePath = relPrefix + name,
            IsDirectory = false,
            DownloadUrl = dl,
            Size = size,
            Sha = sha
        });
    }

    private static bool IsIgnoredDir(string name) =>
        name is "src" or "bin" or "obj" or "node_modules" or ".git" or ".vs"
             or ".idea" or ".vscode";

    // ---------------------------------------------------------------- 下载

    public async Task<byte[]> DownloadAsync(RepoEntry entry, CancellationToken ct)
    {
        // 优先用 API 提供的 download_url；没有就按 raw 规约拼
        var url = entry.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = _preset.RawUrl(entry.Path);

        return await Http.GetByteArrayAsync(url, ct);
    }

    public async Task<byte[]?> DownloadPackageAsync(RepoPackage package, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(package.Url)) return null;
        try { return await Http.GetByteArrayAsync(package.Url, ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gitea] 下载更新包失败: {ex.Message}");
            return null;
        }
    }
}
