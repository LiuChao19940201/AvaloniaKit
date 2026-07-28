using System.Text.Json;

namespace AvaloniaKit.Extensions;

internal static class JsonExtensions
{
    internal static string? TryGetStr(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Null) return null;
        return v.ToString();
    }

    internal static long TryGetLong(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long val)) return val;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out long sval)) return sval;
        return 0;
    }

    /// <summary>取字符串属性；数值等其他类型返回原始文本（基金接口字段类型不稳定）</summary>
    internal static string? TryGet(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString() ?? v.GetRawText()
            : null;
}
