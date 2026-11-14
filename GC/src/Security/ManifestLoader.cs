using System;
using System.Text.Json;

namespace GC.Services;

/// <summary>
/// 把「远端拿到的清单文本」变成 <see cref="RepoVersion"/>，同时带上验签结论。
///
/// 设计上把「验签」和「允不允许用」分开：
///   · 本方法只负责**如实报告**信任状态；
///   · 允不允许安装由 <c>UpdateService</c> / <c>LauncherUpdater</c> 按策略决定。
/// 这样开发态可以「警告但继续」，发布版可以「直接拒绝」，而解析逻辑只有一份。
/// </summary>
public static class ManifestLoader
{
    public static RepoVersion? Parse(string? text, out ManifestVerification verification)
    {
        verification = ManifestSignature.Verify(text, requireSignature: true);
        if (string.IsNullOrWhiteSpace(text)) return null;

        RepoVersion? version;
        try
        {
            version = JsonSerializer.Deserialize<RepoVersion>(text!, Json.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"版本清单解析失败: {ex.Message}");
            return null;
        }

        if (version == null) return null;

        // 平台键回填：RepoPackage 自己要能说出「我属于哪个 RID」，
        // 否则下载端无法推导 dist/<系统>-<架构> 这类兜底路径。
        foreach (var pair in version.Launcher.Packages)
            if (pair.Value != null) pair.Value.Rid = pair.Key;

        version.SignatureTrust = verification.Trust.ToString();
        version.SignatureKeyId = verification.KeyId;
        version.SignatureError = verification.Error;
        return version;
    }
}
