using System;
using System.Net;
using System.Net.Http;

namespace GC.Services;

/// <summary>
/// 共享 HttpClient 工厂：统一管理连接池，避免每个类创建独立的 HttpClient 实例。
///
/// .NET Core 3.1+ 的 HttpClientHandler 内部使用连接池，但每个 HttpClient 实例仍会创建
/// 独立的 Socket 连接池。多个静态 HttpClient 实例（尤其是不同超时配置）会导致端口耗尽
/// 或连接无法及时释放。通过单一实例 + 共享 Handler 解决此问题。
///
/// 注意：不同场景对超时要求差异较大（探测 6s、下载 5min），通过构造参数传递超时，
/// 但底层 Handler 共享，避免重复创建 Socket。
/// </summary>
public static class SharedHttpClientFactory
{
    /// <summary>默认 HTTP 客户端（短超时，用于探测和快速请求）。</summary>
    private static readonly HttpClient DefaultClient = CreateClient(TimeSpan.FromSeconds(20));

    /// <summary>长超时 HTTP 客户端（用于大文件下载）。</summary>
    private static readonly HttpClient LongTimeoutClient = CreateClient(TimeSpan.FromMinutes(5));

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate,
            // 限制最大重定向次数，防止重定向循环
            MaxAutomaticRedirections = 5
        };
        var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = timeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GC/1.0");
        return client;
    }

    /// <summary>获取默认超时（20秒）的 HttpClient，用于常规 API 请求。</summary>
    public static HttpClient GetDefault() => DefaultClient;

    /// <summary>获取长超时（5分钟）的 HttpClient，用于大文件下载。</summary>
    public static HttpClient GetLongTimeout() => LongTimeoutClient;
}
