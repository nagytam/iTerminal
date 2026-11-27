namespace iTerminal.DevelopmentStudio.Model;

/// <summary>
/// An error or warning a language server reported for an open file. <see cref="Path"/> is the full path, and
/// <see cref="Line"/> and <see cref="Column"/> are 1-based (the server's positions are 0-based).
/// </summary>
public sealed record LiveDiagnostic(string Path, int Line, int Column, bool IsError, string Code, string Message);
