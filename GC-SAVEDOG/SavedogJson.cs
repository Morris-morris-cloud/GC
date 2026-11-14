using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using GC.Services;

namespace GC.Savedog;

/// <summary>
/// GC-SAVEDOG 是裁剪发布的（PublishTrimmed），反射式序列化在裁剪后会失效：
/// trimmer 认为「没人 new 过」的构造函数会被删掉，运行时反序列化直接抛
/// <c>NotSupportedException</c>。因此守护进程把所有 JSON 类型都注册进源生成上下文，
/// 编译期生成类型信息，运行时零反射。
///
/// 新增需要读写的 JSON 类型时，记得在这里补一行 <c>[JsonSerializable]</c>，
/// 否则会在裁剪产物里报错（未裁剪的调试构建看不出问题）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(SavedogRequest))]
[JsonSerializable(typeof(SavedogState))]
[JsonSerializable(typeof(LastGoodVersion))]
[JsonSerializable(typeof(StartupHealth.HealthRecord))]
[JsonSerializable(typeof(ModelIntegrityFile))]
[JsonSerializable(typeof(VerifyReport))]
[JsonSerializable(typeof(StatusReport))]
[JsonSerializable(typeof(VerifyModelReport))]
internal partial class SavedogJsonContext : JsonSerializerContext
{
    // 源生成器已提供 (JsonSerializerOptions) 构造函数；实例化时传入启动器的那套选项，
    // 让两边写出的 JSON 完全同款（中文不转成 \uXXXX、缩进一致），便于直接 diff 排查。
}

/// <summary><c>verify --json</c> 的输出。</summary>
internal sealed class VerifyReport
{
    public string Trust { get; set; } = "";
    public string? KeyId { get; set; }
    public string? Error { get; set; }
    public List<TrustedKey> TrustedKeys { get; set; } = new();
}

internal sealed class TrustedKey
{
    public string KeyId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

/// <summary><c>status --json</c> 的输出。</summary>
internal sealed class StatusReport
{
    public SavedogState? State { get; set; }
    public LastGoodVersion? LastGood { get; set; }
    public StartupHealth.HealthRecord? Health { get; set; }
}

/// <summary><c>verify-model --json</c> 的输出。</summary>
internal sealed class VerifyModelReport
{
    public bool Ok { get; set; }
    public bool Present { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>把共享的 <see cref="Json"/> 接到源生成上下文上。</summary>
internal static class SavedogJson
{
    private static readonly SavedogJsonContext Context = new(new JsonSerializerOptions(Json.Options));

    public static void Initialize()
    {
        Json.TypeInfoProvider = type => Context.GetTypeInfo(type);
    }
}
