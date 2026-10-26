using iConsole.Data;
using iConsole.Display;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iConsole.Drivers;

public class SystemConsoleDriver : IConsoleDriver
{
    private Position _windowPosition = new(0, 0);

    private string _title = string.Empty;
    public string Title
    {
        get
        {
            return _title;
        }
        set
        {
            if (_title != value)
            {
                _title = value;
                try
                {
                    Console.Title = _title;
                }
                catch (IOException)
                {
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }
    }

    private bool _isCursorVisible = false;
    public bool IsCursorVisible 
    {
        get 
        { 
            return _isCursorVisible; 
        } 
        set
        {
            if (_isCursorVisible != value)
            {
                _isCursorVisible = value;
                try
                {
                    Console.CursorVisible = _isCursorVisible;
                }
                catch (IOException)
                {
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }  
    }

    private Color _foregroundColor;
    public Color ForegroundColor
    {
        get
        {
            return _foregroundColor;
        }
        set
        {
            if (_foregroundColor != value)
            {
                _foregroundColor = value;
                try
                {
                    Console.ForegroundColor = (ConsoleColor)value;
                }
                catch (System.IO.IOException)
                {
                }
            }
        }
    }

    private Color _backgroundColor;
    public Color BackgroundColor
    {
        get
        {
            return _backgroundColor;
        }
        set
        {
            if (_backgroundColor != value)
            {
                _backgroundColor = value;
                try
                {
                    Console.BackgroundColor = (ConsoleColor)value;
                }
                catch (System.IO.IOException)
                {
                }
            }
        }
    }

    private Size _windowSize;
    public Size WindowSize => _windowSize;

    // Set when the console accepts escape sequences. Plain cells keep going through the Console colour calls; a cell with a
    // text attribute (bold, dim, curly underline…) or a hyperlink is written as ANSI, which the Console API cannot express.
    private readonly bool _supportsVirtualTerminal;

    public SystemConsoleDriver()
    {
        _supportsVirtualTerminal = TryEnableVirtualTerminal();
        // Unit tests (and some hosts) can run without an attached console, where WindowWidth/Height throws.
        // Fall back to a minimal size so color/title APIs can still be exercised.
        try
        {
            _windowSize = ConsoleWindowSizeHelper.GetVisibleWindowSize();
            _windowPosition = ConsoleWindowSizeHelper.GetVisibleWindowPosition();
        }
        catch (System.IO.IOException)
        {
            _windowSize = new Size(0, 0);
            _windowPosition = new Position(0, 0);
        }
    }

    /// <summary>Default colours, a visible cursor at the top left of a cleared screen: what a shell expects to start on.</summary>
    public void OnConsoleReleased()
    {
        try
        {
            Console.ResetColor();
            Console.Clear();
            Console.SetCursorPosition(0, 0);
            Console.CursorVisible = true;
            _isCursorVisible = true;
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The colours this driver believes are set are put back — the child left whatever it liked, and
    /// a cached colour that no longer matches the console is one the next write would skip setting.
    /// </summary>
    public void OnConsoleReacquired()
    {
        try
        {
            Console.ForegroundColor = (ConsoleColor)_foregroundColor;
            Console.BackgroundColor = (ConsoleColor)_backgroundColor;
            Console.CursorVisible = false;
            _isCursorVisible = false;
            Console.Clear();
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        RefreshWindowSize();
    }

    public void Beep()
    {
        // Console.Beep() plays a tone *synchronously*, blocking the calling thread for the
        // full duration (~200 ms on Windows). Because Beep() is invoked from the input-consumer
        // thread on every unhandled key, that stall freezes all key handling and lets queued
        // keystrokes pile up. The BEL control character produces the same audible feedback via
        // the terminal without blocking (this is what VirtualSequenceConsoleDriver already does).
        Console.Write('\a');
    }

    public void RefreshWindowSize()
    {
        try
        {
            _windowSize = ConsoleWindowSizeHelper.GetVisibleWindowSize();
            _windowPosition = ConsoleWindowSizeHelper.GetVisibleWindowPosition();
        }
        catch (System.IO.IOException)
        {
            _windowSize = new Size(0, 0);
            _windowPosition = new Position(0, 0);
        }
    }

    public void Clear()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    public Position GetCursorPosition()
    {
        try
        {
            var (left, top) = Console.GetCursorPosition();
            return ConsoleWindowSizeHelper.GetVisiblePosition(new Position(left, top), _windowPosition);
        }
        catch (IOException)
        {
            return new Position(0, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new Position(0, 0);
        }
    }

    public void SetCursorPosition(int x, int y)
    {
        Debug.Assert(x >= 0 && x < WindowSize.Width);
        Debug.Assert(y >= 0 && y < WindowSize.Height);
        try
        {
            var bufferPosition = ConsoleWindowSizeHelper.GetBufferPosition(new Position(x, y), _windowPosition);
            Console.SetCursorPosition((int)bufferPosition.X, (int)bufferPosition.Y);
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    public void Write(int x, int y, Conxel conxel)
    {
        try
        {
            SetCursorPosition(x, y);
            ForegroundColor = conxel.ForegroundColor;
            BackgroundColor = conxel.BackgroundColor;
            Console.Write(conxel.Char);
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    // Reused across frames to avoid per-write allocations. Write is only ever called from the
    // single console-renderer thread, so a shared, non-synchronized builder is safe here.
    private readonly System.Text.StringBuilder _writeBuilder = new(256);

    public void Write(int x, int y, ConxelString s)
    {
        var list = s.String;
        if (list.Count == 0) return;
        if (_supportsVirtualTerminal && NeedsAnsi(list))
        {
            WriteAnsi(x, y, s);
            return;
        }

        try
        {
            SetCursorPosition(x, y);

            // Coalesce consecutive cells that share fore/background colour into a single
            // Console.Write(string) call. The previous implementation wrote one character at a
            // time, so a full-screen redraw issued thousands of synchronized Console.Write calls
            // (each a flush/syscall) plus a colour-property set per cell — the dominant cost of a
            // visible refresh. Writing whole same-colour runs cuts that to a handful of calls per
            // line. The cursor auto-advances after each run, so no per-character repositioning is
            // needed within a contiguous string.
            _writeBuilder.Clear();
            var runFg = list[0].ForegroundColor;
            var runBg = list[0].BackgroundColor;
            foreach (var c in list)
            {
                if (c.ForegroundColor != runFg || c.BackgroundColor != runBg)
                {
                    FlushRun(runFg, runBg);
                    runFg = c.ForegroundColor;
                    runBg = c.BackgroundColor;
                }
                _writeBuilder.Append(c.Char);
            }
            FlushRun(runFg, runBg);
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static bool NeedsAnsi(List<Conxel> list)
    {
        foreach (var conxel in list)
        {
            if (AnsiRenderer.NeedsAnsi(conxel))
            {
                return true;
            }
        }

        return false;
    }

    private void WriteAnsi(int x, int y, ConxelString s)
    {
        try
        {
            // The sequences position the cursor themselves, relative to the window, and end with a reset.
            Console.Out.Write(AnsiRenderer.ConxelString2Ansi(x, y, s));

            // The reset put the console's own colours back to its defaults; the cached colours no longer describe it, and
            // the setters skip a value they think is already set. Setting them again keeps the two in step.
            Console.ForegroundColor = (ConsoleColor)_foregroundColor;
            Console.BackgroundColor = (ConsoleColor)_backgroundColor;
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static bool TryEnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            const int StdOutputHandle = -11;
            const uint EnableVirtualTerminalProcessing = 0x0004;
            nint handle = GetStdHandle(StdOutputHandle);
            return GetConsoleMode(handle, out uint mode)
                && ((mode & EnableVirtualTerminalProcessing) != 0 || SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    private void FlushRun(Color foreground, Color background)
    {
        if (_writeBuilder.Length == 0) return;
        ForegroundColor = foreground;
        BackgroundColor = background;
        Console.Out.Write(_writeBuilder);
        _writeBuilder.Clear();
    }
}
