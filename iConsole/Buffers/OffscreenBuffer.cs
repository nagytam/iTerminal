using iConsole.Data;
using iConsole.Display;

namespace iConsole.Buffers;

/// <summary>
/// A buffer that keeps what is written to it in memory instead of on a screen, for a control that draws a child somewhere
/// else than where it is shown (<see cref="Controls.ScrollView"/>). It covers <c>Size</c> cells starting at an
/// <c>Origin</c> in screen coordinates; a write outside that rectangle is dropped. Layers are not kept apart: a later
/// write over an earlier one wins, which is the order the tree draws in.
/// </summary>
internal sealed class OffscreenBuffer : IBuffer
{
    private Conxel?[,] _cells = new Conxel?[0, 0];

    public OffscreenBuffer(Position origin, Size size)
    {
        Origin = origin;
        Size = size;
        Cursor = new Cursor(size);
        Reset(origin, size);
    }

    public Position Origin { get; private set; }

    public Lock SyncLock { get; } = new();

    public Size Size { get; private set; }

    public Cursor Cursor { get; set; }

    public ConcurrentList<BufferLayer> Layers { get; } = [];

    public BufferLayer MergedLayer => new(0, Size, Color.White, Color.Black);

    public int Width => (int)Size.Width;

    public int Height => (int)Size.Height;

    /// <summary>The cell at (<paramref name="x"/>, <paramref name="y"/>) relative to the origin, or null when nothing wrote it.</summary>
    public Conxel? this[int x, int y] => x >= 0 && y >= 0 && x < Width && y < Height ? _cells[x, y] : null;

    /// <summary>Forgets everything and covers a new rectangle. The memory is reused when the size is the same.</summary>
    public void Reset(Position origin, Size size)
    {
        Origin = origin;
        if (_cells.GetLength(0) != size.Width || _cells.GetLength(1) != size.Height)
        {
            _cells = new Conxel?[size.Width, size.Height];
        }
        else
        {
            Array.Clear(_cells);
        }

        Size = size;
        Cursor = new Cursor(size);
    }

    public void Clear() => Array.Clear(_cells);

    public void Resize(Size newSize) => Reset(Origin, newSize);

    public void Write(int zOrder, Position position, Size size, ConxelString conxelString)
    {
        long y = position.Y - Origin.Y;
        if (y < 0 || y >= Height)
        {
            return;
        }

        int count = (int)Math.Min(conxelString.Count, size.Width);
        for (int i = 0; i < count; i++)
        {
            long x = position.X + i - Origin.X;
            if (x >= Width)
            {
                break;
            }

            if (x >= 0)
            {
                _cells[x, y] = conxelString[i];
            }
        }
    }

    /// <summary>Remembers the cursor in screen coordinates, as a control on the screen would set it.</summary>
    public void SetCursorPosition(Position position, bool isVisible)
    {
        Cursor.Size = new Size(Origin.X + Width, Origin.Y + Height);
        Cursor.CursorPosition = position;
        Cursor.IsCursorVisible = isVisible;
    }

    public void Merge()
    {
    }

    public IEnumerable<int> UpdateLayers(int maxZOrder) => [];
}
