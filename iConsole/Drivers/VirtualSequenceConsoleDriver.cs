using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iConsole.Drivers;

public class VirtualSequenceConsoleDriver : IConsoleDriver
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 4;
    private readonly bool _supportsVirtualTerminalProcessing;
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
                Console.Title = _title;
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
                if (_isCursorVisible)
                {
                    Console.Write(AnsiRenderer.SHOW_CURSOR);
                }
                else
                {
                    Console.Write(AnsiRenderer.HIDE_CURSOR);
                }
            }
        }  
    }

    private CursorShape _cursorShape;

    public CursorShape CursorShape
    {
        get => _cursorShape;
        set
        {
            if (_cursorShape == value)
            {
                return;
            }

            _cursorShape = value;
            if (_supportsVirtualTerminalProcessing && CursorShapes.IsEnabledByEnvironment)
            {
                Console.Write(CursorShapes.Sequence(value));
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
                Console.ForegroundColor = (ConsoleColor)value;
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
                Console.BackgroundColor = (ConsoleColor)value;
            }
        }
    }

    private Size _windowSize;
    public Size WindowSize => _windowSize;

    public VirtualSequenceConsoleDriver()
    {
        EnsureConsoleAvailable();

        // Some test runners/hosts don't provide a real console. Accessing Console.WindowWidth/WindowHeight
        // can throw IOException("The handle is invalid.") in that case, so initialize defensively.
        try
        {
            _windowSize = ConsoleWindowSizeHelper.GetVisibleWindowSize();
            _windowPosition = ConsoleWindowSizeHelper.GetVisibleWindowPosition();
        }
        catch (IOException)
        {
            _windowSize = new(0, 0);
            _windowPosition = new(0, 0);
        }

        _supportsVirtualTerminalProcessing = !OperatingSystem.IsWindows();
        if (OperatingSystem.IsWindows())
        {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(handle, out uint mode))
            {
                mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                _supportsVirtualTerminalProcessing = SetConsoleMode(handle, mode);
            }
        }

        // AnsiRenderer.Test();
    }

    public void RefreshWindowSize()
    {
        try
        {
            _windowSize = ConsoleWindowSizeHelper.GetVisibleWindowSize();
            _windowPosition = ConsoleWindowSizeHelper.GetVisibleWindowPosition();
        }
        catch (IOException)
        {
            EnsureConsoleAvailable();
            try
            {
                _windowSize = ConsoleWindowSizeHelper.GetVisibleWindowSize();
                _windowPosition = ConsoleWindowSizeHelper.GetVisibleWindowPosition();
            }
            catch (IOException)
            {
                // Keep the last known size when no console is attached.
            }
        }
    }

    private static void EnsureConsoleAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = Console.WindowWidth;
        }
        catch (IOException)
        {
            AllocConsole();
        }
    }

    public void Clear()
    {
        if (_supportsVirtualTerminalProcessing)
        {
            Console.Write(AnsiRenderer.CLEAR_DISPLAY);
        }
        else
        {
            try
            {
                Console.Clear();
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Attributes reset, cursor home and shown, screen cleared: what a shell expects to start on.</summary>
    public void OnConsoleReleased()
    {
        // The shell gets the cursor shape it had before.
        CursorShape = CursorShape.Default;
        if (_supportsVirtualTerminalProcessing)
        {
            Console.Write("\x1B[0m" + AnsiRenderer.CLEAR_DISPLAY + "\x1B[H" + AnsiRenderer.SHOW_CURSOR);
        }
        else
        {
            try
            {
                Console.ResetColor();
                Console.Clear();
                Console.CursorVisible = true;
            }
            catch (IOException)
            {
            }
        }

        Console.Out.Flush();
        _isCursorVisible = true;
    }

    /// <summary>
    /// The child may have switched virtual-terminal processing off on its way out (Windows); it is
    /// switched back on before anything is written, and the screen is cleared for the repaint.
    /// </summary>
    public void OnConsoleReacquired()
    {
        if (OperatingSystem.IsWindows() && _supportsVirtualTerminalProcessing)
        {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(handle, out uint mode))
            {
                SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
            }
        }

        if (_supportsVirtualTerminalProcessing)
        {
            Console.Write("\x1B[0m" + AnsiRenderer.HIDE_CURSOR + AnsiRenderer.CLEAR_DISPLAY);
        }
        else
        {
            Clear();
        }

        Console.Out.Flush();
        _isCursorVisible = false;
        RefreshWindowSize();
    }

    private bool _frameOpen;

    public void BeginFrame()
    {
        if (_supportsVirtualTerminalProcessing && SynchronizedOutput.IsEnabledByEnvironment && !_frameOpen)
        {
            Console.Write(SynchronizedOutput.Begin);
            _frameOpen = true;
        }
    }

    public void EndFrame()
    {
        if (_frameOpen)
        {
            Console.Write(SynchronizedOutput.End);
            Console.Out.Flush();
            _frameOpen = false;
        }
    }

    public void Beep()
    {
        Console.Write(AnsiRenderer.BELL);
    }

    public void WriteGraphics(int x, int y, string sequence)
    {
        if (_supportsVirtualTerminalProcessing)
        {
            Console.Write($"\u001b[{y + 1};{x + 1}H{sequence}");
        }
    }

    public Position GetCursorPosition()
    {
        var (left, top) = Console.GetCursorPosition();
        return ConsoleWindowSizeHelper.GetVisiblePosition(new Position(left, top), _windowPosition);
    }

    public void SetCursorPosition(int x, int y)
    {
        //Debug.Assert(x >= 0 && x < WindowSize.Width);
        //Debug.Assert(y >= 0 && y < WindowSize.Height);
        x = x.Range(0, Math.Max(0, (int)WindowSize.Width - 1));
        y = y.Range(0, Math.Max(0, (int)WindowSize.Height - 1));
        var bufferPosition = ConsoleWindowSizeHelper.GetBufferPosition(new Position(x, y), _windowPosition);
        try
        {
            Console.SetCursorPosition((int)bufferPosition.X, (int)bufferPosition.Y);
        }
        catch (ArgumentOutOfRangeException)
        {

        }
    }

    public void Write(int x, int y, Conxel conxel)
    {
        if (_supportsVirtualTerminalProcessing)
        {
            var s = AnsiRenderer.Conxel2Ansi(x, y, conxel);
            Console.Write(s);
            return;
        }

        SetCursorPosition(x, y);
        Console.ForegroundColor = (ConsoleColor)conxel.ForegroundColor;
        Console.BackgroundColor = (ConsoleColor)conxel.BackgroundColor;
        Console.Write(conxel.Char);
    }

    public void Write(int x, int y, ConxelString s)
    {
        if (_supportsVirtualTerminalProcessing)
        {
            var ansi = AnsiRenderer.ConxelString2Ansi(x, y, s);
            Console.Write(ansi);
            return;
        }

        for (var index = 0; index < s.Count; index++)
        {
            Write(x + index, y, s[index]);
        }
    }

    #region
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    #endregion
}
