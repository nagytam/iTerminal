namespace iTerminal.DevelopmentStudio.Model;

/// <summary>A command the Development Studio runs: the executable, its arguments and the working folder.</summary>
public sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>The command as a shell user would type it, for the output log.</summary>
    public string Display => string.Join(' ', new[] { FileName }.Concat(Arguments.Select(QuoteIfNeeded)));

    private static string QuoteIfNeeded(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
