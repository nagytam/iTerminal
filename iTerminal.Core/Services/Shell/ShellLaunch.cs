using System.Diagnostics;

namespace iTerminal.Core.Services.Shell;

/// <summary>
/// An interactive shell to hand the terminal to with Ctrl+O: the program, its arguments, the local
/// folder it starts in, and a few words for where it runs ("web:/etc", "C:\src").
/// </summary>
public sealed record ShellLaunch(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory, string Location)
{
    /// <summary>The settings key naming the program Ctrl+O starts on the local disk; empty for the default.</summary>
    public const string ShellSettingKey = "Shell";

    /// <summary>
    /// The line written before the program starts. Null for the shell's own ("shell in … — type exit
    /// to return"); empty for none, which is what a full-screen program such as an editor wants —
    /// it has no <c>exit</c> to type, and paints over the line anyway.
    /// </summary>
    public string? Banner { get; init; }

    public ProcessStartInfo ToStartInfo()
    {
        var startInfo = new ProcessStartInfo(FileName) { UseShellExecute = false };
        foreach (var argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(WorkingDirectory) && Directory.Exists(WorkingDirectory))
        {
            startInfo.WorkingDirectory = WorkingDirectory;
        }

        return startInfo;
    }

    /// <summary>
    /// The user's shell in <paramref name="folder"/>: <paramref name="configuredShell"/> when one is
    /// set; otherwise PowerShell 7 when it is on PATH and <c>%ComSpec%</c> (cmd) when not, on
    /// Windows — Windows PowerShell 5 takes seconds to start, and a key that hands over the terminal
    /// should feel instant — and <c>$SHELL</c> elsewhere.
    /// </summary>
    public static ShellLaunch Local(string folder, string? configuredShell = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredShell))
        {
            return new ShellLaunch(configuredShell.Trim(), [], folder, folder);
        }

        if (OperatingSystem.IsWindows())
        {
            return FindOnPath("pwsh.exe") is { } pwsh
                ? new ShellLaunch(pwsh, ["-NoLogo"], folder, folder)
                : new ShellLaunch(Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec ? comSpec : "cmd.exe", [], folder, folder);
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        return new ShellLaunch(string.IsNullOrEmpty(shell) ? "/bin/sh" : shell, [], folder, folder);
    }

    /// <summary>
    /// A shell inside a running container, in <paramref name="path"/>: bash when the image has it,
    /// sh when it does not (Alpine and distroless-ish images have only BusyBox). <c>-it</c> is what
    /// gives the shell a terminal and its prompt; the drive's own commands run without it.
    /// </summary>
    public static ShellLaunch Docker(string container, string path) => new(
        "docker",
        ["exec", "-it", "-w", path, container, "sh", "-c", "if command -v bash >/dev/null 2>&1; then exec bash; fi; exec sh"],
        WorkingDirectory: null,
        $"{container}:{path}");

    /// <summary>
    /// A login shell on <paramref name="host"/> in <paramref name="path"/>, through the system's own
    /// <c>ssh</c> with <c>-t</c> for a terminal — so it authenticates the way the user's ssh does
    /// (agent, keys, config, a password asked on the terminal), not with the drive's saved credentials.
    /// </summary>
    public static ShellLaunch Ssh(string? user, string host, string path)
    {
        var destination = string.IsNullOrEmpty(user) ? host : $"{user}@{host}";
        var remote = path.Length > 0 ? $"cd {QuotePosix(path)} && exec \"$SHELL\" -l" : "exec \"$SHELL\" -l";
        return new ShellLaunch("ssh", ["-t", destination, remote], WorkingDirectory: null, $"{destination}:{path}");
    }

    /// <summary>A single-quoted POSIX shell word: everything literal, a quote closed, escaped and reopened.</summary>
    internal static string QuotePosix(string text) => "'" + text.Replace("'", "'\\''") + "'";

    internal static string? FindOnPath(string name)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        return null;
    }
}

/// <summary>
/// A panel that knows where Ctrl+O's shell belongs when it is the focused one: its folder on the
/// local disk, the container it is inside, the host it is connected to. A panel that is not one
/// gets a local shell in the home folder.
/// </summary>
public interface IShellLaunchSource
{
    /// <param name="configuredShell">The <see cref="ShellLaunch.ShellSettingKey"/> setting, for a panel that starts a local shell.</param>
    ShellLaunch? GetShellLaunch(string? configuredShell);
}
