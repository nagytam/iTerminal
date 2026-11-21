using System.Text.Json;
using iConsole.Json;

namespace iConsole.Formats.Json;

/// <summary>
/// Checks that a document is valid JSON (RFC 8259), as <see cref="System.Text.Json"/> reads it: comments and trailing
/// commas are not accepted. A document is not checked against a JSON Schema; that needs a schema library.
/// </summary>
public sealed class JsonValidator : IJsonValidator
{
    /// <inheritdoc/>
    public JsonValidationIssue? Validate(byte[] content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = JsonTree.MaxDepth });
            return null;
        }
        catch (JsonException exception)
        {
            // The exception counts lines and columns from zero.
            return new JsonValidationIssue((int)(exception.LineNumber ?? 0) + 1, (int)(exception.BytePositionInLine ?? 0) + 1, exception.Message);
        }
    }
}
