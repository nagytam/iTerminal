namespace iConsole.Json;

/// <summary>Where a document stops being valid JSON, and why.</summary>
public sealed record JsonValidationIssue(int Line, int Column, string Message)
{
    public override string ToString() => $"line {Line}, column {Column}: {Message}";
}
