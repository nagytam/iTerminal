using System.Runtime.InteropServices;

namespace iConsole.Graphics;

/// <summary>A picture fitted to a room: its size in pixels and the cells it covers.</summary>
public readonly record struct SixelFit(int PixelWidth, int PixelHeight, int Columns, int Rows);

/// <summary>
/// What the terminal can draw besides characters: whether it speaks Sixel, and how large a cell is in pixels (a picture is
/// sized in pixels, the screen in cells, so the two must be related). Support is read from the environment — a query would have
/// to share stdin with the key reader — and <c>ITERMINAL_SIXEL</c> (<c>1</c> / <c>0</c>) overrides it.
/// <c>ITERMINAL_CELL_SIZE</c> (<c>WxH</c>) overrides the cell size, which the terminal itself reports where the input reader
/// can ask (Linux); elsewhere it is estimated, and an estimate errs small so that a picture never spills out of its room.
/// </summary>
public sealed class TerminalGraphics
{
    public const int DefaultCellWidth = 8;
    public const int DefaultCellHeight = 16;

    /// <summary>The largest picture drawn, in pixels; a bigger room gets a picture of this size at most, to bound the encoding time.</summary>
    public const int MaxPixels = 2_000_000;

    private static TerminalGraphics? _current;

    private readonly bool _isDetected;
    private int _cellWidth;
    private int _cellHeight;
    private bool _isCellSizeKnown;

    public TerminalGraphics(bool isSixelSupported, int cellWidth = DefaultCellWidth, int cellHeight = DefaultCellHeight, bool isCellSizeKnown = false)
    {
        _isDetected = isSixelSupported;
        _cellWidth = Math.Max(1, cellWidth);
        _cellHeight = Math.Max(1, cellHeight);
        _isCellSizeKnown = isCellSizeKnown;
    }

    /// <summary>The terminal the app runs on. Detected on first use; a test replaces it.</summary>
    public static TerminalGraphics Current
    {
        get => _current ??= Detect();
        set => _current = value;
    }

    /// <summary>
    /// The feature flag: asked on every use whether Sixel may be used at all. The app points it at the <c>SixelEnabled</c> setting;
    /// null (the default) means yes. The flag only ever narrows what was detected, it cannot turn on a terminal that lacks Sixel.
    /// </summary>
    public static Func<bool>? EnabledSource { get; set; }

    /// <summary>True when the terminal draws Sixel and the feature flag allows it.</summary>
    public bool IsSixelSupported => _isDetected && (EnabledSource?.Invoke() ?? true);

    public int CellWidth => Volatile.Read(ref _cellWidth);

    public int CellHeight => Volatile.Read(ref _cellHeight);

    /// <summary>True when the size came from the terminal or the user rather than a guess.</summary>
    public bool IsCellSizeKnown => Volatile.Read(ref _isCellSizeKnown);

    /// <summary>Records the cell size the terminal reported (<c>ESC [ 6 ; height ; width t</c>).</summary>
    public void SetCellSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 200 || height > 400)
        {
            return;
        }

        Volatile.Write(ref _cellWidth, width);
        Volatile.Write(ref _cellHeight, height);
        Volatile.Write(ref _isCellSizeKnown, true);
    }

    /// <summary>
    /// The largest picture of the given aspect ratio (width / height) that fits <paramref name="areaColumns"/> × <paramref name="areaRows"/>
    /// cells, in pixels, and the cells it covers.
    /// </summary>
    public SixelFit Fit(double aspect, int areaColumns, int areaRows)
    {
        if (areaColumns <= 0 || areaRows <= 0)
        {
            return new SixelFit(0, 0, 0, 0);
        }

        if (aspect <= 0)
        {
            aspect = 16.0 / 9.0;
        }

        int cellWidth = CellWidth;
        int cellHeight = CellHeight;
        double width = (double)areaColumns * cellWidth;
        double height = (double)areaRows * cellHeight;
        double scale = width * height > MaxPixels ? Math.Sqrt(MaxPixels / (width * height)) : 1;
        width *= scale;
        height *= scale;

        if (width / aspect > height)
        {
            width = height * aspect;
        }
        else
        {
            height = width / aspect;
        }

        int pixelWidth = Math.Max(1, (int)Math.Floor(width));
        int pixelHeight = Math.Max(1, (int)Math.Floor(height));
        int columns = Math.Clamp((pixelWidth + cellWidth - 1) / cellWidth, 1, areaColumns);
        int rows = Math.Clamp((pixelHeight + cellHeight - 1) / cellHeight, 1, areaRows);
        return new SixelFit(pixelWidth, pixelHeight, columns, rows);
    }

    /// <summary>Reads the environment of the running process.</summary>
    public static TerminalGraphics Detect()
    {
        bool redirected;
        try
        {
            redirected = Console.IsOutputRedirected || Console.IsInputRedirected;
        }
        catch (IOException)
        {
            redirected = true;
        }

        bool sixel = !redirected && DetectSixel(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());
        if (TryParseCellSize(Environment.GetEnvironmentVariable("ITERMINAL_CELL_SIZE"), out int width, out int height))
        {
            return new TerminalGraphics(sixel, width, height, isCellSizeKnown: true);
        }

        if (sixel && OperatingSystem.IsWindows())
        {
            double scale = WindowsScale();
            return new TerminalGraphics(true, (int)(DefaultCellWidth * scale), (int)(DefaultCellHeight * scale));
        }

        return new TerminalGraphics(sixel);
    }

    /// <summary>
    /// Whether the terminal named by the environment draws Sixel. Terminals that do not say so in the environment (a plain xterm,
    /// which needs <c>-ti vt340</c>) are left out; <c>ITERMINAL_SIXEL=1</c> turns it on for them.
    /// </summary>
    public static bool DetectSixel(Func<string, string?> environment, bool isWindows)
    {
        string? forced = environment("ITERMINAL_SIXEL");
        if (!string.IsNullOrWhiteSpace(forced))
        {
            return forced.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";
        }

        // A multiplexer sits between the app and the terminal and passes Sixel only if it was built to.
        if (!string.IsNullOrEmpty(environment("TMUX")) || !string.IsNullOrEmpty(environment("STY")))
        {
            return false;
        }

        string term = environment("TERM") ?? string.Empty;
        string program = environment("TERM_PROGRAM") ?? string.Empty;

        // Windows Terminal draws Sixel from 1.22; it names itself with WT_SESSION. The plain console host does not.
        if (isWindows && !string.IsNullOrEmpty(environment("WT_SESSION")))
        {
            return true;
        }

        if (program is "WezTerm" or "iTerm.app" || term.Contains("sixel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (term.StartsWith("foot", StringComparison.Ordinal)
            || term.StartsWith("mlterm", StringComparison.Ordinal)
            || term.StartsWith("contour", StringComparison.Ordinal)
            || term.StartsWith("yaft", StringComparison.Ordinal))
        {
            return true;
        }

        // Konsole has drawn Sixel since 22.04, and says its version as 220400.
        return int.TryParse(environment("KONSOLE_VERSION"), out int konsole) && konsole >= 220400;
    }

    /// <summary>Reads <c>WxH</c> (also <c>W,H</c> or <c>W;H</c>).</summary>
    public static bool TryParseCellSize(string? text, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(['x', 'X', ',', ';'], StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out width)
            && int.TryParse(parts[1], out height)
            && width is > 0 and <= 200
            && height is > 0 and <= 400;
    }

    /// <summary>The display scaling of the window hosting the console, 1 when it cannot be read; a larger scale means larger cells.</summary>
    private static double WindowsScale()
    {
        try
        {
            nint console = GetConsoleWindow();
            if (console == 0)
            {
                return 1;
            }

            // Under Windows Terminal the console window is a hidden stand-in; its root owner is the terminal's own window.
            nint owner = GetAncestor(console, 3);
            uint dpi = GetDpiForWindow(owner != 0 ? owner : console);
            return dpi >= 96 ? Math.Min(3, dpi / 96.0) : 1;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
