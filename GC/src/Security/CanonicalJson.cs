using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GC.Services;

/// <summary>
/// 规范化 JSON：签名与验签必须对完全相同的字节做运算，
/// 否则「同一份内容、不同缩进/键序」会得出不同签名。
///
/// 规则（写进 README，第三方可复现）：
///   1. 对象键按 Unicode 码点升序递归排序
///   2. 无缩进、无多余空白，UTF-8 编码，不带 BOM
///   3. 数字沿用源文本（不做浮点重排）
///   4. 非 ASCII 字符不转义（Encoder = UnsafeRelaxedJsonEscaping）
///   5. 签名时先移除根级 <c>signature</c> 字段
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>把 JSON 文本规范化；解析失败返回 null。</summary>
    public static byte[]? Normalize(string text, bool dropSignatureField)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var node = JsonNode.Parse(text);
            if (node is not JsonObject obj) return null;
            if (dropSignatureField) obj.Remove("signature");
            var sorted = SortNode(obj);
            return Encoding.UTF8.GetBytes(sorted?.ToJsonString(Options) ?? "{}");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode? SortNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    result[pair.Key] = SortNode(pair.Value);
                return result;

            case JsonArray array:
                var list = new JsonArray();
                foreach (var item in array) list.Add(SortNode(item));
                return list;

            default:
                return node?.DeepClone();
        }
    }
}
