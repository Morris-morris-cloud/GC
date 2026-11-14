using System;
using System.Collections.Generic;

namespace GC.Services;

/// <summary>
/// 受信任的签名公钥。
///
/// 公钥是公开的，写在源码里没有问题；**私钥永远不进仓库**
/// （放在仓库根 <c>.signing/</c>，已在 .gitignore 中）。
///
/// 轮换密钥：用 <c>GC-SAVEDOG keygen</c> 生成新的一对，把新公钥追加到这里，
/// 旧公钥保留一个版本周期，避免老客户端直接失效。
/// 也可用环境变量临时注入（测试 / 私有构建）：
/// <c>GC_TRUSTED_KEYS="id1:base64;id2:base64"</c>
/// </summary>
public static class SignatureKeyring
{
    public sealed record KeyEntry(string KeyId, string PublicKeyBase64, string? Note = null);

    private static readonly List<KeyEntry> KeysInternal = new()
    {
        // 生产密钥（2026-11 生成）。指纹 b41b1dd6259ea9ed
        new KeyEntry(
            "gc-2026-11",
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEny5YKVBZUzOyRcSiuutlGmBb4+xm" +
            "PrNDGshilBlpmFyWu5Atq+FGP2tn+8Or8V7OH+/7lhCmXu8jZuC5/5CWSw==",
            "GC 主发布密钥")
    };

    static SignatureKeyring() => LoadFromEnvironment();

    public static IReadOnlyList<KeyEntry> Keys => KeysInternal;

    /// <summary>是否配置了受信任密钥（配置了就必须验签）。</summary>
    public static bool Enforced => KeysInternal.Count > 0;

    public static KeyEntry? Find(string? keyId)
    {
        if (KeysInternal.Count == 0) return null;
        foreach (var key in KeysInternal)
            if (string.Equals(key.KeyId, keyId, StringComparison.Ordinal))
                return key;
        // 未写 keyId 且只有一个密钥时容忍（早期构建产物）
        if (string.IsNullOrWhiteSpace(keyId) && KeysInternal.Count == 1) return KeysInternal[0];
        return null;
    }

    /// <summary>测试 / 私有分发用：追加一个受信任公钥。</summary>
    public static void Add(KeyEntry key)
    {
        if (string.IsNullOrWhiteSpace(key.KeyId) || string.IsNullOrWhiteSpace(key.PublicKeyBase64)) return;
        if (Find(key.KeyId) != null) return;
        KeysInternal.Add(key);
    }

    /// <summary>测试用：替换整个密钥环。</summary>
    public static void ReplaceAll(IEnumerable<KeyEntry> keys)
    {
        KeysInternal.Clear();
        foreach (var key in keys) KeysInternal.Add(key);
    }

    private static void LoadFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("GC_TRUSTED_KEYS");
        if (string.IsNullOrWhiteSpace(raw)) return;
        foreach (var part in raw!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf(':');
            if (idx <= 0 || idx >= part.Length - 1) continue;
            var keyId = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            if (keyId.Length == 0 || value.Length == 0) continue;
            if (Find(keyId) != null) continue;
            KeysInternal.Add(new KeyEntry(keyId, value, "来自 GC_TRUSTED_KEYS"));
        }
    }
}
