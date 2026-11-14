using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GC.Services;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// 源生成类型信息的提供者（可选）。
    ///
    /// 启动器本身不做裁剪，走默认的反射序列化即可；GC-SAVEDOG 会被 PublishTrimmed
    /// 裁掉无参构造，反射序列化在运行时直接抛 <c>NotSupportedException</c>。
    /// 因此守护进程在启动时把本钩子指向 <c>JsonSerializerContext</c>，
    /// 让所有读写都走编译期生成的 <see cref="JsonTypeInfo{T}"/>。
    /// 这样「安全模块只有一份实现」的前提没有被破坏，只是换了元数据来源。
    /// </summary>
    public static Func<Type, JsonTypeInfo?>? TypeInfoProvider { get; set; }

    public static T? Read<T>(string path)
    {
        if (!System.IO.File.Exists(path)) return default;
        return Deserialize<T>(System.IO.File.ReadAllText(path));
    }

    public static void Write<T>(string path, T value)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, Serialize(value));
        System.IO.File.Move(tmp, path, true);
    }

    public static T? Deserialize<T>(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var info = TypeInfoProvider?.Invoke(typeof(T)) as JsonTypeInfo<T>;
        return info != null
            ? JsonSerializer.Deserialize(text, info)
            : JsonSerializer.Deserialize<T>(text, Options);
    }

    public static string Serialize<T>(T value)
    {
        var info = TypeInfoProvider?.Invoke(typeof(T)) as JsonTypeInfo<T>;
        return info != null
            ? JsonSerializer.Serialize(value, info)
            : JsonSerializer.Serialize(value, Options);
    }
}
