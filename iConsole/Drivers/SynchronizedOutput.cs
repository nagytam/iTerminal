namespace iConsole.Drivers;

/// <summary>
/// Synchronized output (DEC private mode 2026). The terminal holds what is written between <see cref="Begin"/>
/// and <see cref="End"/> and shows it as one frame, so a large repaint does not tear. A terminal that does not
/// know the mode ignores both sequences.
/// </summary>
public static class SynchronizedOutput
{
    /// <summary>CSI ? 2026 h — start holding output.</summary>
    public const string Begin = "\x1b[?2026h";

    /// <summary>CSI ? 2026 l — show everything written since <see cref="Begin"/>.</summary>
    public const string End = "\x1b[?2026l";

    /// <summary>Set by <c>ITERMINAL_SYNC_OUTPUT=0</c> to switch the sequences off, and off for the Linux console and a dumb terminal.</summary>
    public static bool IsEnabledByEnvironment { get; set; } = DetectSupport();

    private static bool DetectSupport()
    {
        if (Environment.GetEnvironmentVariable("ITERMINAL_SYNC_OUTPUT") == "0")
        {
            return false;
        }

        string? term = Environment.GetEnvironmentVariable("TERM");
        return term is not ("linux" or "dumb");
    }
}
