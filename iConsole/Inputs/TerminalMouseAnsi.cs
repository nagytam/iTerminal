namespace iConsole.Inputs;

/// <summary>
/// XTerm-style mouse reporting (CSI ?1006h SGR extended coordinates).
/// Safe to emit on VT-capable terminals; ignored if unsupported.
/// Shared by Linux TTY input and optionally Windows VT stdin later.
/// </summary>
public static class TerminalMouseAnsi
{
    /// <summary>CSI ?1006h — SGR extended mouse mode.</summary>
    public const string EnableSgrExtendedMouse = "\x1b[?1006h";

    /// <summary>CSI ?1002h — report mouse movement while a button is held.</summary>
    public const string EnableCellMotionMouse = "\x1b[?1002h";

    /// <summary>CSI ?1000h — basic X10 compatibility.</summary>
    public const string EnableBasicMouse = "\x1b[?1000h";

    public const string DisableSgrExtendedMouse = "\x1b[?1006l";
    public const string DisableCellMotionMouse = "\x1b[?1002l";
    public const string DisableBasicMouse = "\x1b[?1000l";

    /// <summary>Preferred sequence: cell motion + SGR coordinates.</summary>
    public static string EnablePreferredMouseReporting =>
        EnableCellMotionMouse + EnableSgrExtendedMouse;

    public static string DisablePreferredMouseReporting =>
        DisableSgrExtendedMouse + DisableCellMotionMouse + DisableBasicMouse;
}
