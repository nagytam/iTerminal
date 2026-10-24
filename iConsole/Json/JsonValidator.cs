using System.Text.Json;

namespace iConsole.Json;

/// <summary>Where a document stops being valid JSON, and why.</summary>
public sealed record JsonValidationIssue(int Line, int Column, string Message)
{
    public override string ToString() => $"line {Line}, column {Column}: {Message}";
}

/// <summary>
/// Checks that a document is valid JSON (RFC 8259), as <see cref="System.Text.Json"/> reads it: comments and trailing
/// commas are not accepted. A document is not checked against a JSON Schema; that needs a schema library.
/// </summary>
public static class JsonValidator
{
    /// <summary>The first place the document is not valid JSON, or null when it is valid.</summary>
    public static JsonValidationIssue? Validate(byte[] content)
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
