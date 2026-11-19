using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GC.Download;
using GC.Models;
using GC.Services;
using GC.UI.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GC.UI.Server;

/// <summary>
/// GC-UI 后端（本地 HTTP 服务：模型扫描 / 下载 / 更新 / 设置 / 模型窗口宿主）。
///
/// 两种跑法共用同一份代码：
///   · 独立进程：<c>GC.UI.Server</c> 可执行文件（开发调试 / 旧包兼容）；
///   · 进程内：启动器直接调用 <see cref="Start"/>，把后端跑在自己的进程里 ——
///     发布包因此只需要一个 exe，不必再复制一份完整的 .NET 运行时。
/// </summary>
public static class BackendServer
{
    /// <summary>按命令行 / GC_UI_PORT 解析端口并构建（未启动）的后端。</summary>
    public static WebApplication StartFromCommandLine(string[] args)
        => Start(args, ResolvePort(args), out _);

    /// <summary>同上，并交出模型窗口宿主（独立进程入口用：主线程留给 Avalonia）。</summary>
    public static WebApplication StartFromCommandLine(string[] args, out AvaloniaHost uiHost)
        => Start(args, ResolvePort(args), out uiHost);

    /// <summary>
    /// 构建后端。返回的 <see cref="WebApplication"/> 尚未启动：
    /// 独立进程直接 <c>Run()</c>，启动器用 <c>StartAsync()</c> 之后继续拉起界面。
    /// </summary>
    public static WebApplication Start(string[] args, int port)
        => Start(args, port, out _);

    /// <summary>
    /// 构建后端，并通过 <paramref name="uiHost"/> 交出模型窗口宿主。
    ///
    /// 调用方（启动器）必须在**主线程**调用 <see cref="AvaloniaHost.RunOnCurrentThread"/>：
    /// macOS 的 AppKit 要求 NSApplication 运行在主线程，否则打开模型窗口时整个进程会崩溃。
    /// </summary>
    public static WebApplication Start(string[] args, int port, out AvaloniaHost uiHost)
    {
        // 下载模块（GC-DOWNLOAD）通过宿主钩子注入，和 GC-BOOT 的做法一致。
        DownloadModuleHost.Factory = new DownloadModuleFactory();

        var services = AppServices.Create();
        var host = new AvaloniaHost(services);
        uiHost = host;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        // 本地伴随进程：限制请求体大小、关闭 Server 头、收紧超时，减少被本地恶意页面
        // 或异常客户端拖垮的攻击面。
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            options.Limits.MaxConcurrentConnections = 64;
        });

        // 桌面端（Windows/macOS）走原生 HTTP，不需要 CORS；
        // 这里放开本机回环来源，方便用 `flutter run -d chrome` 调试界面。
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "::1"))
            .AllowAnyHeader()
            .AllowAnyMethod()));

        var app = builder.Build();
        app.UseCors();

        // 日志与启动器共用 .GC/logs，整条启动链路（启动器 → 后端 → 守护进程）都在同一组文件里。
        var logPaths = AppPaths.Discover();
        GcLog.Init(logPaths.LogsRoot);
        GcLog.Info($"GC-UI 后端启动，监听 127.0.0.1:{port}",
                   $"GC-UI backend started on 127.0.0.1:{port}");

        // 设置白名单 / 标识校验用到的规则（只在本进程内使用）。
        var allowedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "auto", "zh-CN", "en" };
        var allowedThemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "light", "dark", "system" };
        var accentHexRegex = new System.Text.RegularExpressions.Regex(
            "^#(?:[0-9A-Fa-f]{6})$", System.Text.RegularExpressions.RegexOptions.Compiled);
        var safeIdRegex = new System.Text.RegularExpressions.Regex(
            "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        // 安全响应头 + Host/Origin 校验（抵御 DNS rebinding 与跨站调用）。
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            headers["Cache-Control"] = "no-store";

            var host = context.Request.Host.Host;
            if (!string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) &&
                !(Uri.TryCreate(origin, UriKind.Absolute, out var parsed) &&
                  (parsed.Host == "127.0.0.1" || parsed.Host == "localhost" || parsed.Host == "::1")))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });

        // 写入类接口做简单限流：单 IP 每秒最多 40 次，防止本地脚本刷爆下载/更新。
        var writeBuckets =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (long Window, int Count)>();
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) ||
                HttpMethods.IsDelete(context.Request.Method))
            {
                var key = context.Connection.RemoteIpAddress?.ToString() ?? "local";
                var now = Environment.TickCount64;

                // 定期丢弃过期窗口：本机连接数不多，但长时间运行后桶只增不减会缓慢泄漏。
                if (writeBuckets.Count > 256)
                {
                    foreach (var entry in writeBuckets)
                    {
                        if (now - entry.Value.Window > 60_000)
                            writeBuckets.TryRemove(entry.Key, out _);
                    }
                }

                var bucket = writeBuckets.AddOrUpdate(
                    key,
                    _ => (now, 1),
                    (_, old) => now - old.Window > 1000 ? (now, 1) : (old.Window, old.Count + 1));

                if (bucket.Count > 40)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await context.Response.WriteAsJsonAsync(ErrorDto.Of("请求过于频繁，请稍后再试"));
                    return;
                }
            }

            await next();
        });

        // ---- 健康检查 -------------------------------------------------------------

        app.MapGet("/api/health", () => Results.Ok(new HealthDto(
            true,
            LauncherRuntimeInfo.CurrentLauncherVersion,
            LauncherRuntimeInfo.CurrentRid,
            host.IsAvailable,
            host.LastError)));

        // ---- 已安装模型 -----------------------------------------------------------

        app.MapGet("/api/models", () =>
            Results.Ok(services.Models.Scan().Select(ToModelDto).ToList()));

        app.MapGet("/api/models/{id}/preview", (string id, HttpResponse response) =>
        {
            if (!IsSafeId(id)) return Results.BadRequest(ErrorDto.Of("模型标识无效"));
            var package = services.Models.Find(id);
            if (package == null || !File.Exists(package.PreviewPath)) return Results.NotFound();

            // 封面是静态图，允许前端缓存：默认全局中间件写的 no-store 会让每次滚动都重新请求。
            // 按扩展名给准确的 MIME —— 统一写 image/png 时，WebP / JPEG 封面在部分平台显示不出来。
            response.Headers.CacheControl = "private, max-age=300";
            return Results.File(package.PreviewPath, GuessImageContentType(package.PreviewPath));
        });

        app.MapPost("/api/models/{id}/launch", async (string id) =>
        {
            if (!IsSafeId(id)) return Results.BadRequest(new LaunchResultDto(false, "模型标识无效"));
            var package = services.Models.Find(id);
            if (package == null) return Results.NotFound(new LaunchResultDto(false, $"没有找到模型 {id}"));

            try
            {
                services.Memory.Refresh();
                var message = await host.LaunchAsync(package);
                return Results.Ok(new LaunchResultDto(true, message));
            }
            catch (Exception ex)
            {
                // 细节只写日志，不回显给调用方，避免泄漏本机路径等内部信息。
                Console.Error.WriteLine($"[launch] 启动模型 {id} 失败: {ex}");
                return Results.Json(new LaunchResultDto(false, "模型启动失败，请查看后端日志"), statusCode: 503);
            }
        });

        app.MapDelete("/api/models/{id}", (string id) =>
        {
            if (!IsSafeId(id)) return Results.BadRequest(ErrorDto.Of("模型标识无效"));
            var ok = services.Models.Delete(id);
            if (ok) services.Models.NotifyChanged();
            return ok
                ? Results.Ok(new LaunchResultDto(true, $"已删除模型 {id}"))
                : Results.NotFound(ErrorDto.Of($"没有找到模型 {id}"));
        });

        // ---- 设置 -----------------------------------------------------------------

        app.MapGet("/api/settings", () => Results.Ok(ToSettingsDto()));

        app.MapPost("/api/settings", (SettingsPatch? patch) =>
        {
            if (patch == null) return Results.BadRequest(ErrorDto.Of("缺少设置内容"));

            // 白名单校验：设置项会写进磁盘并被前端直接使用，不接受任意字符串。
            if (patch.Language != null && !allowedLanguages.Contains(patch.Language))
                return Results.BadRequest(ErrorDto.Of("不支持的界面语言"));
            if (patch.Theme != null && !allowedThemes.Contains(patch.Theme))
                return Results.BadRequest(ErrorDto.Of("不支持的主题"));
            if (patch.AccentColor != null && !accentHexRegex.IsMatch(patch.AccentColor))
                return Results.BadRequest(ErrorDto.Of("主题色需为 #RRGGBB"));
            if (patch.UpdateRepository != null && !IsAllowedRepository(patch.UpdateRepository))
                return Results.BadRequest(ErrorDto.Of("更新仓库地址不受信任"));

            services.Settings.Apply(s =>
            {
                if (patch.Language != null) s.Language = patch.Language;
                if (patch.Theme != null) s.Theme = patch.Theme;
                if (patch.AccentColor != null) s.AccentColor = patch.AccentColor.ToUpperInvariant();
                if (patch.RenderFps.HasValue) s.RenderFps = Math.Clamp(patch.RenderFps.Value, 12, 120);
                if (patch.MemoryMb.HasValue) s.MemoryMb = Math.Clamp(patch.MemoryMb.Value, 256, 16384);
                if (patch.MemoryAuto.HasValue) s.MemoryAuto = patch.MemoryAuto.Value;
                if (patch.AutoUpdateOnStartup.HasValue) s.AutoUpdateOnStartup = patch.AutoUpdateOnStartup.Value;
                if (patch.RequireSignedManifest.HasValue) s.RequireSignedManifest = patch.RequireSignedManifest.Value;
                if (patch.UpdateRepository != null) s.UpdateRepository = patch.UpdateRepository;
                if (patch.UpdateLocalRoot != null) s.UpdateLocalRoot = patch.UpdateLocalRoot;
                if (patch.ModelRootOverride != null) s.ModelRootOverride = patch.ModelRootOverride;
            });

            if (patch.Language != null) L.I.SetLanguage(patch.Language);
            if (patch.MemoryMb.HasValue || patch.MemoryAuto.HasValue)
                services.Memory.ApplyAuto(services.Settings.Current.MemoryAuto, services.Settings.Current.MemoryMb);

            return Results.Ok(ToSettingsDto());
        });

        // ---- 模型源 ---------------------------------------------------------------

        app.MapGet("/api/sources", () =>
            Results.Ok(services.Settings.Current.Sources.Select(ToSourceDto).ToList()));

        app.MapPost("/api/sources/select", async (ForceRequest? request) =>
        {
            var force = request?.Force ?? false;
            try
            {
                var fastest = await services.Selector.PickFastestAsync(force, CancellationToken.None);
                services.Updates.UseRemote(services.DownloadModule.CreateRemote(fastest));
                return Results.Ok(new LatencyDto(
                    fastest.Id, fastest.Name,
                    services.Selector.LastLatency.TryGetValue(fastest.Id, out var ms) ? ms : 0));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[sources] 切换模型源失败: {ex}");
                return Results.Json(ErrorDto.Of("模型源切换失败"), statusCode: 503);
            }
        });

        app.MapGet("/api/remote/models", async (string? sourceId, bool force) =>
        {
            var source = ResolveSource(sourceId);
            if (source == null) return Results.NotFound(ErrorDto.Of("没有可用的模型源"));

            var index = await services.Sources.FetchAsync(source, force, null, CancellationToken.None);
            var installed = services.Models.Scan()
                .ToDictionary(p => p.Id, p => p.Manifest.Version, StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new SourceIndexDto(
                index.SourceId,
                index.Root,
                index.FromCache,
                index.Error,
                index.ResolvedFrom,
                index.Models.Select(m => new RemoteModelDto(
                    m.Id,
                    m.LocalizedName,
                    m.LocalizedDescription,
                    m.Manifest.Version,
                    m.SizeBytes,
                    ModelScanner.HumanSize(m.SizeBytes),
                    m.SourceId,
                    installed.ContainsKey(m.Id),
                    installed.TryGetValue(m.Id, out var v) ? v : null)).ToList()));
        });

        // ---- 下载 -----------------------------------------------------------------

        app.MapPost("/api/download", async (DownloadRequest? request) =>
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ModelId))
                return Results.BadRequest(ErrorDto.Of("缺少 modelId"));

            var source = ResolveSource(request.SourceId);
            if (source == null) return Results.NotFound(ErrorDto.Of("没有可用的模型源"));

            var index = await services.Sources.FetchAsync(source, false, null, CancellationToken.None);
            var model = index.Models.FirstOrDefault(m =>
                m.Id.Equals(request.ModelId, StringComparison.OrdinalIgnoreCase));
            if (model == null)
                return Results.NotFound(ErrorDto.Of($"源里没有模型 {request.ModelId}"));

            var job = services.Downloads.Enqueue(model);
            return Results.Ok(ToJobDto(job));
        });

        app.MapGet("/api/download/jobs", () =>
            Results.Ok(services.Downloads.Jobs.Select(ToJobDto).ToList()));

        app.MapPost("/api/download/{id}/cancel", (string id) =>
            ActOnJob(id, job => services.Downloads.Cancel(job)));

        app.MapPost("/api/download/{id}/retry", (string id) =>
            ActOnJob(id, job => services.Downloads.Retry(job)));

        app.MapPost("/api/download/{id}/remove", (string id) =>
            ActOnJob(id, job => services.Downloads.Remove(job)));

        app.MapPost("/api/download/clear-finished", () =>
        {
            services.Downloads.ClearFinished();
            return Results.Ok(new LaunchResultDto(true, "已清除完成的任务"));
        });

        // ---- 更新 -----------------------------------------------------------------

        app.MapPost("/api/update/check", async (ForceRequest? request) =>
        {
            var force = request?.Force ?? true;
            var messages = new List<string>();

            try
            {
                var fastest = await services.Selector.PickFastestAsync(force, CancellationToken.None);
                services.Updates.UseRemote(services.DownloadModule.CreateRemote(fastest));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[update] 测速失败，沿用当前源: {ex.Message}");
            }

            string? launcherLatest = null;
            try
            {
                launcherLatest = await services.LauncherUpdates.CheckAsync(
                    services.Updates.Remote, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[update] 启动器版本检查失败: {ex.Message}");
            }

            var progress = new Progress<UpdateProgress>(p =>
            {
                if (!string.IsNullOrWhiteSpace(p.Message)) messages.Add(p.Message);
            });

            var result = await services.Updates.RunAsync(force, progress, CancellationToken.None);

            return Results.Ok(new UpdateCheckDto(
                result.Outcome.ToString(),
                result.Message,
                result.UpdatedModels,
                result.ModelNames,
                result.LauncherMessage,
                result.Error,
                launcherLatest,
                services.Updates.Remote.DisplayName,
                services.Selector.LastLatency
                    .Select(kv => new LatencyDto(
                        kv.Key,
                        SourcePreset.Find(kv.Key)?.Name ?? kv.Key,
                        kv.Value))
                    .ToList()));
        });

        // ---- 关于 -----------------------------------------------------------------

        app.MapGet("/api/about", async () =>
        {
            var repository = services.Settings.Current.UpdateRepository;
            var contributors = new List<ContributorDto>();
            try
            {
                foreach (var c in await ContributorsService.FetchAsync(repository, CancellationToken.None))
                    contributors.Add(new ContributorDto(c.DisplayName, c.Contributions, c.AvatarUrl));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[about] 读取贡献者失败: {ex.Message}");
            }

            return Results.Ok(new AboutDto(
                "Morris GC",
                LauncherRuntimeInfo.CurrentLauncherVersion,
                LauncherRuntimeInfo.CurrentRid,
                repository,
                contributors,
                // 「关于」页展示的技术栈信息：源码语言与前端框架。
                SourceLanguage: "C#",
                Frontend: "Flutter"));
        });

        // ---- 系统集成 -------------------------------------------------------------

        app.MapPost("/api/shell/open-folder", (PathRequest? request) =>
        {
            var path = request?.Path;
            if (string.IsNullOrWhiteSpace(path))
                return Results.BadRequest(ErrorDto.Of("缺少 path"));

            // 只允许打开启动器自己的目录，避免前端被当成任意路径执行器
            if (!IsInsideKnownRoot(path!))
                return Results.BadRequest(ErrorDto.Of("只允许打开启动器自己的目录"));

            Shell.OpenFolder(path!);
            return Results.Ok(new LaunchResultDto(true, "已打开目录"));
        });

        app.MapPost("/api/shell/open-url", (UrlRequest? request) =>
        {
            var url = request?.Url;
            if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return Results.BadRequest(ErrorDto.Of("URL 无效"));

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest(ErrorDto.Of("只允许 http/https 链接"));

            if (!string.IsNullOrEmpty(uri.UserInfo))
                return Results.BadRequest(ErrorDto.Of("URL 不允许携带账号密码"));

            Shell.OpenUrl(url!);
            return Results.Ok(new LaunchResultDto(true, "已打开链接"));
        });

        // ---- 启动 -----------------------------------------------------------------

        WriteEndpointFile(services, port);
        Console.WriteLine($"[GC-UI] 后端已启动: http://127.0.0.1:{port}");
        Console.WriteLine($"[GC-UI] 模型库: {services.Paths.ModelRoot}");

        return app;

        // ---------------------------------------------------------------------------
        // 局部辅助
        // ---------------------------------------------------------------------------



        static void WriteEndpointFile(AppServices services, int port)
        {
            try
            {
                var file = Path.Combine(services.Paths.HomeRoot, "ui-backend.json");
                var payload = JsonSerializer.Serialize(new
                {
                    port,
                    pid = Environment.ProcessId,
                    url = $"http://127.0.0.1:{port}",
                    version = LauncherRuntimeInfo.CurrentLauncherVersion,
                    platform = LauncherRuntimeInfo.CurrentRid,
                    startedAt = DateTimeOffset.UtcNow.ToString("o")
                }, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(file, payload);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GC-UI] 写入端点文件失败: {ex.Message}");
            }
        }

        /// <summary>按扩展名推断图片 MIME；模型封面允许 png / jpg / webp / gif。</summary>
        static string GuessImageContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };

        ModelDto ToModelDto(ModelPackage package) => new(    package.Id,
            package.LocalizedName,
            package.LocalizedDescription,
            package.Manifest.Version,
            package.Manifest.Author,
            package.SizeBytes,
            ModelScanner.HumanSize(package.SizeBytes),
            package.Manifest.Tags ?? new List<string>(),
            File.Exists(package.PreviewPath),
            $"/api/models/{package.Id}/preview",
            package.Directory,
            package.DataDirectory);

        SourceDto ToSourceDto(ModelSource source) => new(
            source.Id,
            string.IsNullOrWhiteSpace(source.Name) ? source.Id : source.Name,
            source.Url,
            source.Enabled,
            source.BuiltIn,
            source.DisplayHost);

        SettingsDto ToSettingsDto() => new(
            services.Settings.Current.Language,
            services.Settings.Current.Theme,
            services.Settings.Current.AccentColor,
            services.Settings.Current.RenderFps,
            services.Settings.Current.MemoryMb,
            services.Settings.Current.MemoryAuto,
            services.Settings.Current.AutoUpdateOnStartup,
            services.Settings.Current.RequireSignedManifest,
            services.Settings.Current.UpdateRepository,
            services.Settings.Current.UpdateLocalRoot,
            services.Settings.Current.ModelRootOverride,
            services.Memory.BudgetMb,
            services.Memory.UsageText,
            services.Paths.ModelRoot,
            services.Paths.DataRoot,
            services.Paths.SettingsFile,
            LauncherRuntimeInfo.CurrentLauncherVersion,
            LauncherRuntimeInfo.CurrentRid,
            services.Settings.Current.Sources.Select(ToSourceDto).ToList(),
            L.Supported.Select(s => new LanguageDto(s.Code, s.Label)).ToList());

        JobDto ToJobDto(IDownloadJob job) => new(
            job.Id,
            job.Model.Id,
            job.Name,
            job.Version,
            job.State.ToString(),
            job.Progress,
            job.StatusText,
            job.DetailText,
            job.Error,
            job.IsActive,
            job.CanRetry,
            job.IsDone);

        ModelSource? ResolveSource(string? sourceId)
        {
            var sources = services.Settings.Current.Sources;
            if (!string.IsNullOrWhiteSpace(sourceId))
            {
                var exact = sources.FirstOrDefault(s =>
                    s.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            return sources.FirstOrDefault(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Url))
                   ?? sources.FirstOrDefault();
        }

        IResult ActOnJob(string id, Action<IDownloadJob> action)
        {
            if (!IsSafeId(id)) return Results.BadRequest(ErrorDto.Of("任务标识无效"));
            var job = services.Downloads.Jobs.FirstOrDefault(j =>
                j.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (job == null) return Results.NotFound(ErrorDto.Of($"没有找到任务 {id}"));

            action(job);
            return Results.Ok(ToJobDto(job));
        }

        bool IsInsideKnownRoot(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var roots = new[]
                {
                    services.Paths.ModelRoot,
                    services.Paths.DataRoot,
                    services.Paths.HomeRoot,
                    services.Paths.AppRoot
                };

                foreach (var root in roots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    var rootFull = Path.GetFullPath(root);
                    if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) return true;
                    if (full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        // 路由标识：只允许字母数字开头，后接字母数字/点/下划线/短横线，长度上限 64。
        // 用于模型 id、任务 id，杜绝 `../` 之类的路径穿越尝试。
        bool IsSafeId(string? id) => !string.IsNullOrEmpty(id) && safeIdRegex.IsMatch(id);

        // 更新仓库地址：必须是 https、不带账号密码、不是回环/内网 IP。
        // 防止把本地后端当成 SSRF 跳板去访问内网服务。
        bool IsAllowedRepository(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
            if (System.Net.IPAddress.TryParse(uri.Host, out var ip) &&
                (System.Net.IPAddress.IsLoopback(ip) || IsPrivateAddress(ip)))
                return false;
            return true;
        }

        static bool IsPrivateAddress(System.Net.IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) return false;
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }
    }

    public static int ResolvePort(string[] args)
    {
        // 优先用启动器传来的端口（GC_UI_PORT），保证前端拼出来的 baseUrl 和这里一致。
        var env = Environment.GetEnvironmentVariable(GcUiContract.PortEnvVar);
        if (int.TryParse(env, out var fromEnv) && fromEnv > 0 && fromEnv < 65536) return fromEnv;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var next) && next > 0 && next < 65536)
                return next;

            const string prefix = "--port=";
            if (args[i].StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(args[i][prefix.Length..], out var inline) && inline > 0 && inline < 65536)
                return inline;
        }

        return GcUiContract.DefaultPort;
    }
}
