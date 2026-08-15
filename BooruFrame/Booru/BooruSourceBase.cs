using System.Net.Http;
using System.Text.Json;

namespace BooruFrame.Booru;

/// <summary>Shared helpers for JSON-based booru sources.</summary>
public abstract class BooruSourceBase
{
    protected readonly HttpClient Http;
    protected readonly string BaseUrl;

    protected BooruSourceBase(HttpClient http, string baseUrl)
    {
        Http = http;
        BaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
    }

    public abstract string Name { get; }

    protected static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    protected static int GetInt(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
                return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s))
                return s;
        }
        return 0;
    }
}
