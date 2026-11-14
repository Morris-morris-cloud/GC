using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GC.Services;

/// <summary>
/// 内容哈希（可复现的指纹算法）。
///
/// 算法固定如下，构建端与客户端必须完全一致，否则会误判为「被篡改」：
/// <code>
///   fileHash  = SHA256(文件字节)
///   dirHash   = SHA256( 按相对路径 ordinal 升序，对每个文件拼接
///                        "<相对路径&gt;\0&lt;fileHash&gt;\n" )
/// </code>
/// 相对路径统一用 <c>/</c> 分隔；跳过以 <c>.</c> 开头的文件、<c>bin/</c>、<c>obj/</c>、
/// <c>*.user</c>；不包含时间戳、权限位、文件顺序等元数据 —— 同样内容必然同样哈希。
/// </summary>
public static class ContentHash
{
    public static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256Hex(string text)
        => Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>目录内所有参与分发的文件 -> { 相对路径 : sha256 }。</summary>
    public static Dictionary<string, string> MapFiles(string directory)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return map;

        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (!Distributable(rel)) continue;
            map[rel] = Sha256File(file);
        }
        return map;
    }

    /// <summary>按上述算法计算目录指纹。</summary>
    public static string DirectoryHash(string directory)
        => DirectoryHash(MapFiles(directory));

    public static string DirectoryHash(IReadOnlyDictionary<string, string> files)
    {
        var builder = new StringBuilder();
        foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
            builder.Append(pair.Key).Append('\0').Append(pair.Value).Append('\n');

        return Sha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    /// <summary>目录指纹；目录不存在时返回空串（区别于空目录的哈希）。</summary>
    public static string DirectoryHashOrEmpty(string directory)
        => Directory.Exists(directory) ? DirectoryHash(directory) : "";

    public static bool Distributable(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/');
        if (rel.StartsWith('.')) return false;
        if (rel.Contains("/.")) return false;
        if (rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)) return false;
        if (rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)) return false;
        if (rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) return false;
        if (rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) return false;
        if (rel.EndsWith(".user", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
