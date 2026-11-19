using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

/// <summary>
/// 带断点续传的流式下载器。已下载的部分写在 <c>*.part</c> 文件里，
/// 完整下载后由调用方把 .part 改成正式文件名。
///
/// 为什么需要它：模型源码 zip 可能有上百 MB，弱网中断后如果每次都从头下，
/// 用户会反复卡在同一个地方。这里用 HTTP Range 从已下载的字节接着下。
///
/// 兼容性：服务器不支持 Range（忽略请求、返回 200）时自动退化为整段下载，
/// 不会卡死或拿到半截文件。<see cref="Content-Range"/> 起始位置不符时也从头下。
///
/// .part 文件只有在"续传"场景才复用；一旦下载完整、调用方改名为正式文件，
/// 下次就是全新下载，不会把旧内容接在新内容后面。
/// </summary>
public static class ResumableDownloader
{
    /// <summary>
    /// 下载 <paramref name="url"/>，数据写入 <paramref name="partPath"/>。
    /// 返回最终 .part 文件的字节数。下载完整后由调用方负责改名 / 校验。
    /// </summary>
    public static async Task<long> DownloadAsync(
        HttpClient http,
        string url,
        string partPath,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct)
    {
        // 已有 .part 就尝试从断点续传；没有则从头下
        long resumeFrom = 0;
        if (File.Exists(partPath))
        {
            try { resumeFrom = new FileInfo(partPath).Length; }
            catch { resumeFrom = 0; }
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
            req.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var canResume = response.StatusCode == HttpStatusCode.PartialContent && resumeFrom > 0;
        long? total = response.Content.Headers.ContentLength;

        if (canResume)
        {
            // 校验服务器确实从我们要的位置开始返回：Content-Range: bytes START-END/TOTAL
            var range = response.Content.Headers.ContentRange;
            if (range?.HasRange == true && range.From == resumeFrom && range.Length.HasValue)
            {
                total = range.Length.Value;
            }
            else
            {
                // 服务器忽略了 Range 或给出的范围对不上：从头覆盖，避免拼接出损坏的文件
                canResume = false;
                resumeFrom = 0;
            }
        }

        var mode = canResume ? FileMode.Append : FileMode.Create;
        var dir = Path.GetDirectoryName(partPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var output = new FileStream(partPath, mode, FileAccess.Write, FileShare.Read,
                         81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long done = resumeFrom;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                // Total 未知时用已下载量当分母，进度条会停在 100% 之前但不报错
                progress?.Report((done, total ?? done));
            }
        }

        return SafeLength(partPath);
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
