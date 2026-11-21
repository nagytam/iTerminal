namespace iConsole.Xml;

/// <summary>One problem a validation found: where it is, whether it is an error or a warning, and what it says.</summary>
public sealed record XmlValidationIssue(int Line, int Column, bool IsError, string Message);
