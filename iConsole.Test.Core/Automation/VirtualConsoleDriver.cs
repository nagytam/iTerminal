using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Core.Automation;

/// <summary>
/// An <see cref="IConsoleDriver"/> with no console behind it: every write lands in an in-memory grid
/// of cells, which is exactly what a user would see in a terminal of <see cref="WindowSize"/>.
///
/// <para>This is the far end of the real render pipeline — <c>ControlRendererJob</c> composes the
/// layers and <c>ConsoleBufferRendererJob</c> diffs them and writes only the changed runs — so a test
/// that reads <see cref="Snapshot"/> sees what the diffing renderer actually put on screen, stale
/// cells included, not what one control believes it drew.</para>
///
/// <para>Thread-safe: the renderer writes from its own thread while a test reads snapshots.</para>
/// </summary>
public sealed class VirtualConsoleDriver : IConsoleDriver
{
    private readonly Lock _lock = new();
    private Conxel[,] _cells;
    private Size _windowSize;
    private Size _pendingSize;
    private Position _cursorPosition = new(0, 0);
    private long _version;
    private int _beepCount;

    public VirtualConsoleDriver(int width = 120, int height = 40)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        _windowSize = new Size(width, height);
        _pendingSize = _windowSize;
        _cells = CreateCells(width, height, Color.White, Color.Black);
    }

    #region IConsoleDriver
    public bool IsCursorVisible { get; set; }
    public Color ForegroundColor { get; set; } = Color.White;
    public Color BackgroundColor { get; set; } = Color.Black;
    public string Title { get; set; } = string.Empty;

    public Size WindowSize
    {
        get
        {
            lock (_lock)
            {
                return new Size(_windowSize.Width, _windowSize.Height);
            }
        }
    }

    public void Beep() => Interlocked.Increment(ref _beepCount);

    public void Clear()
    {
        lock (_lock)
        {
            _cells = CreateCells((int)_windowSize.Width, (int)_windowSize.Height, ForegroundColor, BackgroundColor);
            _version++;
        }
    }

    public Position GetCursorPosition()
    {
        lock (_lock)
        {
            return new Position(_cursorPosition.X, _cursorPosition.Y);
        }
    }

    /// <summary>Applies a size requested by <see cref="Resize"/>, the way a real terminal reports a drag.</summary>
    public void RefreshWindowSize()
    {
        lock (_lock)
        {
            if (_pendingSize.Width == _windowSize.Width && _pendingSize.Height == _windowSize.Height)
            {
                return;
            }

            var resized = CreateCells((int)_pendingSize.Width, (int)_pendingSize.Height, ForegroundColor, BackgroundColor);
            for (var y = 0; y < Math.Min(_windowSize.Height, _pendingSize.Height); y++)
            {
                for (var x = 0; x < Math.Min(_windowSize.Width, _pendingSize.Width); x++)
                {
                    resized[x, y] = _cells[x, y];
                }
            }

            _cells = resized;
            _windowSize = new Size(_pendingSize.Width, _pendingSize.Height);
            _version++;
        }
    }

    public void SetCursorPosition(int x, int y)
    {
        lock (_lock)
        {
            _cursorPosition = new Position(
                Math.Clamp(x, 0, (int)_windowSize.Width - 1),
                Math.Clamp(y, 0, (int)_windowSize.Height - 1));
        }
    }

    public void Write(int x, int y, Conxel conxel)
    {
        lock (_lock)
        {
            if (y >= 0 && y < _windowSize.Height && x >= 0 && x < _windowSize.Width)
            {
                _cells[x, y] = conxel;
                _version++;
            }
        }
    }

    public void Write(int x, int y, ConxelString s)
    {
        lock (_lock)
        {
            if (y < 0 || y >= _windowSize.Height)
            {
                return;
            }

            for (var i = 0; i < s.Count; i++)
            {
                var column = x + i;
                if (column >= 0 && column < _windowSize.Width)
                {
                    _cells[column, y] = s[i];
                }
            }

            _version++;
        }
    }
    #endregion

    /// <summary>Number of times the application rang the bell — an unhandled key does that.</summary>
    public int BeepCount => Volatile.Read(ref _beepCount);

    /// <summary>Moves on every write, clear and resize; equal versions mean an unchanged screen.</summary>
    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Asks for a new terminal size. Like dragging a real window, it takes effect when the renderer
    /// next polls the size (every 100 ms), which is what makes the app re-layout.
    /// </summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        lock (_lock)
        {
            _pendingSize = new Size(width, height);
        }
    }

    /// <summary>An immutable copy of the screen as it is now.</summary>
    public ConsoleSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new ConsoleSnapshot(
                (Conxel[,])_cells.Clone(),
                new Position(_cursorPosition.X, _cursorPosition.Y),
                IsCursorVisible,
                _version);
        }
    }

    private static Conxel[,] CreateCells(int width, int height, Color foreground, Color background)
    {
        var cells = new Conxel[width, height];
        var blank = new Conxel(' ', foreground, background);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[x, y] = blank;
            }
        }
        return cells;
    }
}
