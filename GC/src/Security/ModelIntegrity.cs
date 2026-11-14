using System;
using System.Collections.Generic;
using System.IO;

namespace GC.Services;

/// <summary>模型安装后落盘的完整性记录。</summary>
public sealed class ModelIntegrityFile
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>模型清单文件的 sha256。</summary>
    public string ManifestSha256 { get; set; } = "";
    /// <summary>&lt;id&gt;.model 目录的目录指纹。</summary>
    public string FilesSha256 { get; set; } = "";
    /// <summary>关键文件（dll / json）逐文件哈希，用于加载前快速校验。</summary>
    public Dictionary<string, string> Files { get; set; } = new();
    public string InstalledAt { get; set; } = "";
    /// <summary>来源（远端源名 / 本地目录），便于排查。</summary>
    public string Source { get; set; } = "";
}

public sealed record IntegrityResult(bool Ok, bool Present, string Message)
{
    public static readonly IntegrityResult Missing =
        new(true, false, "没有完整性记录（本地/开发模型），跳过校验");
}

/// <summary>
/// 模型包完整性校验：下载后复算、安装后落盘、加载前抽查。
///
/// 威胁模型：模型 dll 会被 <c>AssemblyLoadContext</c> 直接执行，等于任意代码。
/// 如果版本清单被篡改（或本地文件被替换），攻击者就能让启动器加载他的 dll。
/// 因此：
///   1. version.json（已签名）里带 manifestSha256 / filesSha256；
///   2. 下载到暂存目录后按同一算法复算，不一致直接丢弃，绝不安装；
///   3. 安装成功后写 <c>&lt;id&gt;.integrity.json</c>；
///   4. 每次加载 dll 前抽查 dll / json 的哈希。
/// </summary>
public static class ModelIntegrity
{
    public const string FileNameSuffix = ".integrity.json";

    public static string IntegrityPath(string modelDirectory, string id)
        => Path.Combine(modelDirectory, id + FileNameSuffix);

    public static ModelIntegrityFile? Read(string modelDirectory, string id)
        => Json.Read<ModelIntegrityFile>(IntegrityPath(modelDirectory, id));

    public static void Write(string modelDirectory, string id, ModelIntegrityFile data)
        => Json.Write(IntegrityPath(modelDirectory, id), data);

    /// <summary>
    /// 计算某个模型包（清单 + 数据目录）的哈希。
    /// <paramref name="packageDirectory"/> 是包含 <c>&lt;id&gt;.json</c> 与
    /// <c>&lt;id&gt;.model/</c> 的那一层。
    /// </summary>
    public static (string ManifestSha256, string FilesSha256, Dictionary<string, string> Files)
        Compute(string packageDirectory, string id)
    {
        var manifestPath = Path.Combine(packageDirectory, id + ".json");
        var dataDir = Path.Combine(packageDirectory, id + ".model");

        var manifestSha = File.Exists(manifestPath) ? ContentHash.Sha256File(manifestPath) : "";
        var files = ContentHash.MapFiles(dataDir);
        var filesSha = ContentHash.DirectoryHash(files);

        var critical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in files)
        {
            if (!IsCritical(pair.Key)) continue;
            critical[id + ".model/" + pair.Key] = pair.Value;
        }
        if (manifestSha.Length > 0) critical[id + ".json"] = manifestSha;

        return (manifestSha, filesSha, critical);
    }

    /// <summary>加载前校验：只查会被执行 / 解析的文件，保证 <c>OpenModel</c> 不被明显拖慢。</summary>
    public static IntegrityResult VerifyForLoad(string packageDirectory, string id)
    {
        var record = Read(packageDirectory, id);
        if (record == null) return IntegrityResult.Missing;

        if (!string.Equals(record.Id, id, StringComparison.Ordinal))
            return new IntegrityResult(false, true, $"完整性记录与模型 id 不符: {record.Id}");

        foreach (var pair in record.Files)
        {
            var path = Path.Combine(packageDirectory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                return new IntegrityResult(false, true, $"缺少文件: {pair.Key}");

            var actual = ContentHash.Sha256File(path);
            if (!string.Equals(actual, pair.Value, StringComparison.OrdinalIgnoreCase))
                return new IntegrityResult(false, true, $"文件已被修改: {pair.Key}");
        }

        return new IntegrityResult(true, true, "完整性校验通过");
    }

    private static bool IsCritical(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/');
        return rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
               rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
               rel.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
               rel.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
    }
}
