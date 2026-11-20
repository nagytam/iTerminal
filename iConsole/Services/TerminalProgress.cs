namespace iConsole.Services;

/// <summary>What the terminal's own progress indicator (OSC 9;4) should show.</summary>
public enum TerminalProgressState
{
    /// <summary>No indicator.</summary>
    Remove,

    /// <summary>A determinate indicator at the given percentage.</summary>
    Normal,

    /// <summary>A moving indicator with no percentage.</summary>
    Indeterminate,

    /// <summary>The given percentage, shown as paused.</summary>
    Paused,
}

/// <summary>
/// OSC 9;4, the progress sequence that Windows Terminal shows in the tab or the taskbar. Pure functions: the service writes
/// what these return.
/// </summary>
public static class TerminalProgressSequences
{
    /// <summary>
    /// The sequence for <paramref name="state"/>. The percentage is clamped to 0 to 100 before it becomes an int, and it is left
    /// out for <see cref="TerminalProgressState.Remove"/> and <see cref="TerminalProgressState.Indeterminate"/>, which carry none.
    /// </summary>
    public static string Progress(TerminalProgressState state, long percentage)
    {
        int code = state switch
        {
            TerminalProgressState.Normal => 1,
            TerminalProgressState.Indeterminate => 3,
            TerminalProgressState.Paused => 4,
            _ => 0,
        };

        if (state is TerminalProgressState.Remove or TerminalProgressState.Indeterminate)
        {
            return "\u001b]9;4;" + code + "\u0007";
        }

        int clamped = (int)Math.Clamp(percentage, 0, 100);
        return "\u001b]9;4;" + code + ";" + clamped + "\u0007";
    }

    /// <summary>
    /// Whether the terminal is taken to understand OSC 9;4. Windows Terminal is detected by <c>WT_SESSION</c>.
    /// <c>ITERMINAL_PROGRESS=on</c> forces it on for another terminal that supports the sequence, and <c>off</c> switches it off.
    /// </summary>
    public static bool SupportsProgress(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        string? forced = getEnvironmentVariable("ITERMINAL_PROGRESS")?.Trim().ToLowerInvariant();
        switch (forced)
        {
            case "off" or "0":
                return false;
            case "on" or "1":
                return true;
        }

        return !string.IsNullOrEmpty(getEnvironmentVariable("WT_SESSION"));
    }
}
