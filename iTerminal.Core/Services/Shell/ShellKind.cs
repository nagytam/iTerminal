namespace iTerminal.Core.Services.Shell;

/// <summary>
/// The shells a command-log session can run: POSIX <c>sh</c> and <c>bash</c>. Each one knows how to
/// start non-interactively, how a command is wrapped so a child cannot read the protocol, and how the
/// session reports its exit code and working directory.
/// </summary>
public enum ShellKind
{
    Sh,
    Bash,
}

public static class ShellKinds
{
    /// <summary>The settings key naming the command-log shell. Not a reserved <c>__</c> key.</summary>
    public const string SettingKey = "TerminalShell";

    /// <summary>The kinds in the order they are offered.</summary>
    public static readonly IReadOnlyList<ShellKind> All = [ShellKind.Sh, ShellKind.Bash];

    public static string Name(ShellKind kind) => kind switch
    {
        ShellKind.Sh => "sh",
        ShellKind.Bash => "bash",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? text, out ShellKind kind)
    {
        foreach (var candidate in All.Where(candidate => string.Equals(Name(candidate), text?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            kind = candidate;
            return true;
        }

        kind = ShellKind.Sh;
        return false;
    }

    /// <summary>The program's arguments: bash without its startup files, so the session is the same on every machine.</summary>
    public static IReadOnlyList<string> LaunchArguments(ShellKind kind) =>
        kind == ShellKind.Bash ? ["--noprofile", "--norc"] : [];

    /// <summary>
    /// A command as written to the session: a group, so a compound line ("cd x &amp;&amp; cat") is one unit,
    /// with stdin taken from <c>/dev/null</c> so a child that reads stdin does not consume the protocol.
    /// The newline before the closing brace ends a trailing comment.
    /// </summary>
    public static string Wrap(string command) => "{ " + command + "\n} </dev/null";

    /// <summary>
    /// The line after a command, carrying the token, the exit status and the working directory. It is
    /// printed to stdout (status and directory) and to stderr (the token alone), so the session knows
    /// both streams have finished the command.
    /// </summary>
    public static string MarkerLine(ShellKind kind, string token) =>
        "__iterm_rc=$?; printf '%s %s %s\\n' " + Quote(token) + " \"$__iterm_rc\" \"$PWD\"; printf '%s %s\\n' " + Quote(token) + " \"$__iterm_rc\" >&2";

    /// <summary>A command that moves the session into <paramref name="folder"/>.</summary>
    public static string ChangeDirectory(ShellKind kind, string folder) => "cd -- " + Quote(folder);

    /// <summary>A POSIX single-quoted word: everything literal, a quote closed, escaped and reopened.</summary>
    public static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";
}
