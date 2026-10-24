using System.Text.Json;
using System.Text.RegularExpressions;
using iConsole.Model;

namespace iConsole.Json;

/// <summary>
/// A node of a JSON document shown in a <see cref="iConsole.Controls.TreeView"/>: an object, an array, or a value. An
/// object or an array with contents has children; a value, or an empty object or array, is a leaf.
/// </summary>
public class JsonNodeItem(JsonNodeItem? parent, string name) : HierarchicalItemModel(parent, name);

/// <summary>Builds the tree of a parsed JSON document. Each row reads as the JSON does: <c>"key": value</c>, <c>[0]: value</c>.</summary>
public static class JsonTree
{
    /// <summary>Longest text shown on one row; a longer string is cut with an ellipsis.</summary>
    public const int MaxTextLength = 120;

    /// <summary>Deepest nesting read; a document nested deeper is reported as not valid JSON.</summary>
    public const int MaxDepth = 256;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The root row of the document, with its children.</summary>
    public static JsonNodeItem Build(JsonDocument document)
    {
        JsonNodeItem root = new(null, Label(null, document.RootElement));
        AddChildren(root, document.RootElement);
        return root;
    }

    private static void AddChildren(JsonNodeItem parent, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    AddChild(parent, property.Name, property.Value);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AddChild(parent, $"[{index++}]", item);
                }

                break;
        }
    }

    private static void AddChild(JsonNodeItem parent, string key, JsonElement value)
    {
        JsonNodeItem item = new(parent, Label(key, value));
        parent.Children.Add(item);
        AddChildren(item, value);
    }

    /// <summary>
    /// The row text for a value: its key (quoted, for a property) and the value. An object or array shows how many members
    /// it has, since its members are its children.
    /// </summary>
    public static string Label(string? key, JsonElement value)
    {
        string prefix = key is null ? string.Empty : key.StartsWith('[') ? $"{key}: " : $"{Quote(key)}: ";
        return prefix + value.ValueKind switch
        {
            JsonValueKind.Object => $"{{ {value.EnumerateObject().Count():N0} }}",
            JsonValueKind.Array => $"[ {value.EnumerateArray().Count():N0} ]",
            JsonValueKind.String => Quote(Shorten(value.GetString() ?? string.Empty)),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null",
        };
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\\\"") + "\"";

    /// <summary>Collapses whitespace, so a string with line breaks reads on one row, and cuts it at <see cref="MaxTextLength"/>.</summary>
    public static string Shorten(string text)
    {
        string collapsed = Whitespace.Replace(text, " ").Trim();
        return collapsed.Length > MaxTextLength ? collapsed[..(MaxTextLength - 1)] + "…" : collapsed;
    }
}
