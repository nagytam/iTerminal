namespace iConsole.Inputs;

/// <summary>
/// Focus reporting (DEC private mode 1004). With it on, a terminal sends <c>CSI I</c> when its window gets the focus and
/// <c>CSI O</c> when it loses it. A terminal that does not know the mode ignores the request and sends nothing.
/// </summary>
public static class TerminalFocusReporting
{
    /// <summary>CSI ? 1004 h — ask for focus reports.</summary>
    public const string Enable = "\u001b[?1004h";

    /// <summary>CSI ? 1004 l — stop them, so the shell does not get <c>^[[I</c> typed into its prompt.</summary>
    public const string Disable = "\u001b[?1004l";

    /// <summary><c>ITERMINAL_FOCUS_REPORTING=0</c> switches the request off, and it is off for the Linux console and a dumb terminal.</summary>
    public static bool IsEnabledByEnvironment { get; set; } = DetectSupport();

    private static bool DetectSupport()
    {
        if (Environment.GetEnvironmentVariable("ITERMINAL_FOCUS_REPORTING") == "0")
        {
            return false;
        }

        string? term = Environment.GetEnvironmentVariable("TERM");
        return term is not ("linux" or "dumb");
    }
}
