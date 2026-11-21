using System.Text.Json.Nodes;

namespace iConsole.Structured;

/// <summary>
/// Parses YAML and TOML text into the JSON-shaped tree the data viewer shows. The libraries are in
/// <c>iConsole.Formats</c>, which implements this interface, so the viewer dialogs here never reference them.
/// </summary>
public interface IStructuredDocumentParser
{
    JsonNode? ParseYaml(string text);

    JsonNode? ParseToml(string text);
}
