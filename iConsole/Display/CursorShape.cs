namespace iConsole.Display;

/// <summary>
/// The shape of the text cursor (DECSCUSR). The value is the code the terminal is sent, so
/// <see cref="Default"/> hands the cursor back to the terminal's own setting.
/// </summary>
public enum CursorShape
{
    /// <summary>The terminal's own shape.</summary>
    Default = 0,

    BlinkingBlock = 1,
    SteadyBlock = 2,
    BlinkingUnderline = 3,
    SteadyUnderline = 4,

    /// <summary>A thin vertical bar between characters: what a text field shows.</summary>
    BlinkingBar = 5,

    SteadyBar = 6,
}

/// <summary>The escape sequence for a cursor shape and whether it is sent at all.</summary>
public static class CursorShapes
{
    /// <summary><c>CSI Ps SP q</c>: sets the cursor shape. A terminal that does not know it ignores it.</summary>
    public static string Sequence(CursorShape shape) => "\u001b[" + (int)shape + " q";

    /// <summary>
    /// Off with <c>ITERMINAL_CURSOR_SHAPE=0</c>, and for the Linux console and a dumb terminal, which would print the
    /// parameters.
    /// </summary>
    public static bool IsEnabledByEnvironment { get; set; } = DetectSupport();

    private static bool DetectSupport()
    {
        if (Environment.GetEnvironmentVariable("ITERMINAL_CURSOR_SHAPE") == "0")
        {
            return false;
        }

        string? term = Environment.GetEnvironmentVariable("TERM");
        return term is not ("linux" or "dumb");
    }
}
