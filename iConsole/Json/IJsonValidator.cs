namespace iConsole.Json;

/// <summary>
/// Checks that a document is valid JSON. The implementation is in <c>iConsole.Formats</c>, so the JSON viewer here never
/// references <c>System.Text.Json</c> parsing for validation.
/// </summary>
public interface IJsonValidator
{
    /// <summary>The first place the document is not valid JSON, or null when it is valid.</summary>
    JsonValidationIssue? Validate(byte[] content);
}
