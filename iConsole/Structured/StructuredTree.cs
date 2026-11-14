using System.Text.Json;
using System.Text.Json.Nodes;
using iConsole.Json;

namespace iConsole.Structured;

/// <summary>
/// The common end of the YAML and TOML viewers. Both read their file into a <see cref="JsonNode"/> tree, and this turns that
/// tree into the <see cref="JsonDocument"/> the JSON viewer's tree is built from, so every structured format shows its rows
/// the way a JSON document does.
/// </summary>
public static class StructuredTree
{
    /// <summary>The most values read from one file. An alias that repeats a large block (a "billion laughs") is cut here.</summary>
    public const int MaxNodes = 200_000;

    /// <summary>A document of the given tree. The tree must already be within <see cref="JsonTree.MaxDepth"/> and <see cref="MaxNodes"/>.</summary>
    public static JsonDocument ToDocument(JsonNode? node) =>
        JsonDocument.Parse(node?.ToJsonString() ?? "null", new JsonDocumentOptions { MaxDepth = JsonTree.MaxDepth });
}
