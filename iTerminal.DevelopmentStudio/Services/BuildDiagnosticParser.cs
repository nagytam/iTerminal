using System.Text.RegularExpressions;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// Reads the errors and warnings out of the output lines of a build, test or run. It knows two shapes: MSBuild and
/// the C# compiler (<c>Program.cs(3,5): error CS1002: ; expected [App.csproj]</c>), and the gcc, clang and javac style
/// (<c>Main.java:3: error: ';' expected</c>, <c>file.c:3:5: error: ...</c>). Any other line is not a diagnostic.
/// </summary>
public static partial class BuildDiagnosticParser
{
    [GeneratedRegex(@"^\s*(?<file>.+?)\((?<line>\d+)(?:,(?<column>\d+))?\)\s*:\s*(?<kind>error|warning)\s+(?<message>.*?)\s*$")]
    private static partial Regex MsBuildShape();

    [GeneratedRegex(@"^\s*(?<file>.+?):(?<line>\d+)(?::(?<column>\d+))?:\s*(?<kind>(?:fatal )?error|warning):\s*(?<message>.*?)\s*$")]
    private static partial Regex CompilerShape();

    [GeneratedRegex(@"\s*\[[^\[\]]*\]$")]
    private static partial Regex TrailingProjectSuffix();

    /// <summary>The diagnostic the line holds, or null when it is not an error or a warning with a file and a line.</summary>
    public static BuildDiagnostic? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        Match match = MsBuildShape().Match(line);
        if (!match.Success) match = CompilerShape().Match(line);
        if (!match.Success) return null;

        string message = TrailingProjectSuffix().Replace(match.Groups["message"].Value, string.Empty).TrimEnd();
        if (message.Length == 0) return null;

        int.TryParse(match.Groups["line"].ValueSpan, out int number);
        int column = match.Groups["column"].Success && int.TryParse(match.Groups["column"].ValueSpan, out int parsed) ? parsed : 0;
        bool isError = match.Groups["kind"].Value.EndsWith("error", StringComparison.Ordinal);
        return new BuildDiagnostic(isError, match.Groups["file"].Value, number, column, message, line.Trim());
    }
}
