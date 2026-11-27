namespace iTerminal.DevelopmentStudio.Model;

/// <summary>
/// One error or warning printed by a build, test or run: the file and the position it names, and the message after the
/// severity. <see cref="Column"/> is 0 when the tool did not print one. <see cref="Text"/> is the output line as printed,
/// which is what the Error List shows.
/// </summary>
public sealed record BuildDiagnostic(bool IsError, string File, int Line, int Column, string Message, string Text);
