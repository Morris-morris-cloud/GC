using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GC.Services;

/// <summary>清单可信度。</summary>
public enum ManifestTrust
{
    /// <summary>签名有效，且密钥在受信任列表里。</summary>
    Verified,
    /// <summary>本机构建没有配置受信任密钥 —— 只能跳过验签（会打警告）。</summary>
    Skipped,
    /// <summary>清单没有签名，但客户端要求签名。</summary>
    Unsigned,
    /// <summary>有签名但验证失败：内容被改过 / 密钥不对 / 结构损坏。</summary>
    Invalid
}

public sealed record ManifestVerification(ManifestTrust Trust, string? KeyId, string? Error)
{
    public bool IsTrusted => Trust is ManifestTrust.Verified or ManifestTrust.Skipped;
    public bool IsRejected => Trust is ManifestTrust.Unsigned or ManifestTrust.Invalid;

    public static ManifestVerification Ok(string keyId) => new(ManifestTrust.Verified, keyId, null);
    public static ManifestVerification Skip() => new(ManifestTrust.Skipped, null,
        "未配置受信任公钥，已跳过验签");
    public static ManifestVerification Bad(string error) => new(ManifestTrust.Invalid, null, error);
}

/// <summary>
/// 版本清单签名（ECDSA P-256 / SHA-256）。
///
/// 为什么不用 sha256 了事：sha256 只能证明「下载没坏」，不能证明「这份清单是我发的」。
/// 仓库被入侵时攻击者可以同时改清单和 sha256 —— 只有签名需要**没有放进仓库**的私钥，
/// 才能把「篡改清单」这条路堵死。
///
/// 为什么不自己实现 Ed25519：手写椭圆曲线实现出错就是静默失效。.NET 8 自带
/// ECDSA P-256，跨平台、无第三方依赖，安全性等价于工程需要。
///
/// 信任链：内置公钥 → 验证 version.json → 得到各发布包的 sha256 → 校验下载到的 zip
/// → 校验解压出的模型文件 → 加载 dll。
/// </summary>
public static class ManifestSignature
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ------------------------------------------------------------------ 验签

    /// <summary>
    /// 校验清单文本的签名。<paramref name="requireSignature"/> 为 true 时，
    /// 无签名视为失败；为 false 时无签名只返回 <see cref="ManifestTrust.Unsigned"/>，
    /// 由调用方决定是否放行（开发态用）。
    /// </summary>
    public static ManifestVerification Verify(string? text, bool requireSignature)
    {
        if (SignatureKeyring.Keys.Count == 0) return ManifestVerification.Skip();
        if (string.IsNullOrWhiteSpace(text)) return ManifestVerification.Bad("清单内容为空");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(text!) as JsonObject ?? throw new JsonException("根节点不是对象");
        }
        catch (Exception ex)
        {
            return ManifestVerification.Bad("清单不是合法 JSON: " + ex.Message);
        }

        if (root["signature"] is not JsonObject signature)
            return requireSignature
                ? new ManifestVerification(ManifestTrust.Unsigned, null, "清单缺少签名")
                : new ManifestVerification(ManifestTrust.Unsigned, null, "清单未签名");

        var keyId = signature["keyId"]?.GetValue<string>() ?? "";
        var value = signature["value"]?.GetValue<string>() ?? "";
        var alg = signature["alg"]?.GetValue<string>() ?? Algorithm;

        if (!string.Equals(alg, Algorithm, StringComparison.OrdinalIgnoreCase))
            return new ManifestVerification(ManifestTrust.Invalid, keyId, $"不支持的签名算法: {alg}");
        if (value.Length == 0)
            return new ManifestVerification(ManifestTrust.Invalid, keyId, "签名为空");

        var key = SignatureKeyring.Find(keyId);
        if (key == null)
            return new ManifestVerification(ManifestTrust.Invalid, keyId, $"未知签名密钥: {keyId}");

        var payload = CanonicalJson.Normalize(text!, dropSignatureField: true);
        if (payload == null)
            return new ManifestVerification(ManifestTrust.Invalid, keyId, "无法规范化清单");

        try
        {
            var sig = Convert.FromBase64String(value);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKeyBase64), out _);
            var ok = ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return ok
                ? ManifestVerification.Ok(key.KeyId)
                : new ManifestVerification(ManifestTrust.Invalid, keyId, "签名与内容不符");
        }
        catch (Exception ex)
        {
            return new ManifestVerification(ManifestTrust.Invalid, keyId, "验签异常: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ 签名

    /// <summary>用私钥给清单签名，返回写入了 <c>signature</c> 字段的新清单文本。</summary>
    public static string SignDocument(string text, ECDsa key, string keyId)
    {
        var payload = CanonicalJson.Normalize(text, dropSignatureField: true)
                      ?? throw new InvalidOperationException("清单不是合法 JSON，无法签名");

        var signature = key.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var contentHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var root = JsonNode.Parse(text) as JsonObject
                   ?? throw new InvalidOperationException("清单根节点不是对象");

        root["signature"] = new JsonObject
        {
            ["alg"] = Algorithm,
            ["keyId"] = keyId,
            ["value"] = Convert.ToBase64String(signature),
            ["contentSha256"] = contentHash,
            ["signedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        return root.ToJsonString(WriteOptions) + "\n";
    }

    /// <summary>从 PEM 文件加载私钥（PKCS#8 或 SEC1 均可）。</summary>
    public static ECDsa? TryLoadPrivateKey(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var pem = File.ReadAllText(path);
            if (pem.Contains("BEGIN", StringComparison.Ordinal) == false) return null;
            var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return key;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>导出公钥（SPKI / Base64），用于写入客户端受信任列表。</summary>
    public static string ExportPublicKeyBase64(ECDsa key)
        => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>公钥指纹，便于人工核对两边的公钥是否一致。</summary>
    public static string PublicKeyFingerprint(string publicKeyBase64)
        => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64)))
            .ToLowerInvariant()[..16];
}
