namespace iConsole.Inputs;

/// <summary>
/// The Kitty keyboard protocol (progressive enhancement). A terminal that supports it reports keys as
/// <c>CSI code ; modifiers u</c>, so Ctrl+I and Tab, Ctrl+Shift+letter and Ctrl+letter, and a lone Esc and
/// the start of an escape sequence are told apart. A terminal that does not know it ignores the query
/// and keeps sending the legacy bytes, which the decoder still reads.
/// </summary>
public static class KittyKeyboardProtocol
{
    /// <summary>Flag 1: report Esc, and keys with modifiers, as <c>CSI u</c> sequences. Plain text still arrives as text.</summary>
    public const int DisambiguateEscapeCodes = 1;

    /// <summary>CSI ? u — asks for the flags in force. A supporting terminal answers <c>CSI ? flags u</c>.</summary>
    public const string Query = "\x1b[?u";

    /// <summary>CSI &gt; 1 u — pushes the disambiguate flag on the terminal's stack.</summary>
    public static string Push => "\x1b[>" + DisambiguateEscapeCodes + "u";

    /// <summary>CSI &lt; u — pops what <see cref="Push"/> pushed, so the shell gets its own keyboard mode back.</summary>
    public const string Pop = "\x1b[<u";

    /// <summary><c>ITERMINAL_KITTY_KEYBOARD=0</c> switches the query and the push off.</summary>
    public static bool IsEnabledByEnvironment =>
        Environment.GetEnvironmentVariable("ITERMINAL_KITTY_KEYBOARD") != "0";
}
