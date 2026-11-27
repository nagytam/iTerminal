namespace iTerminal.Core.Data;

/// <summary>
/// A breakpoint the Development studio has set, by file and line (1-based). The table is the studio's own store: the breakpoints
/// are kept across start-ups, and a row is written when a breakpoint is set and removed when it is cleared.
/// </summary>
public class DevelopmentBreakpointEntry
{
    /// <summary>The file's full path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The line the breakpoint is on, counted from 1.</summary>
    public int Line { get; set; }
}
