using System.Text.Json;

namespace iConsole.Har;

/// <summary>Reads named properties from a HAR entry's JSON without throwing: a missing or differently typed value reads as empty.</summary>
public static class HarJson
{
    /// <summary>The property's value, or an undefined element when the object has no such property (or is not an object).</summary>
    public static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;

    public static string Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;

    public static double? Number(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out double number) ? number : null;

    public static long? Integer(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out long number) ? number : null;
}
