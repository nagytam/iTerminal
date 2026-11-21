using System.Text.Json.Nodes;
using iConsole.Structured;
using iConsole.Formats.Toml;
using iConsole.Formats.Yaml;

namespace iConsole.Formats;

/// <summary>The YAML and TOML parsers behind the data viewer's <see cref="IStructuredDocumentParser"/>.</summary>
public sealed class StructuredDocumentParser : IStructuredDocumentParser
{
    public JsonNode? ParseYaml(string text) => YamlDocumentReader.Read(text);

    public JsonNode? ParseToml(string text) => TomlDocumentReader.Read(text);
}
