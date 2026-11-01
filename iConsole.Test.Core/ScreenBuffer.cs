using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace iConsole.Test.Core;

/// <summary>
/// An <see cref="IBuffer"/> for asserting on what a control actually <i>drew</i>: every cell keeps
/// its character, colours and style flags, and each Z-order is its own layer, composed on read the
/// way the renderer merges them (the highest layer that wrote a cell wins).
///
/// <para><see cref="MockBuffer"/> keeps text only, which cannot tell a focused row from an
/// unfocused one or a disabled field from an enabled one — the whole of a control's visual state
/// lives in the colours. And because nothing clears a layer between frames in the real renderer, a
/// control must repaint every cell of its rectangle; drawing twice into one
/// <see cref="ScreenBuffer"/> is how a test catches the stale text a shrinking control leaves
/// behind.</para>
/// </summary>
public sealed class ScreenBuffer : IBuffer
{
    private readonly SortedDictionary<int, Conxel?[,]> _layers = [];

    public ScreenBuffer() : this(80, 25)
    {
    }

    public ScreenBuffer(int width, int height)
    {
        Size = new Size(width, height);
        Cursor = new Cursor(Size);
    }

    public Color DefaultForeground { get; set; } = Color.White;
    public Color DefaultBackground { get; set; } = Color.Black;

    public int Width => (int)Size.Width;
    public int Height => (int)Size.Height;

    #region IBuffer
    public Lock SyncLock { get; } = new();
    public Size Size { get; private set; }
    public Cursor Cursor { get; set; }
    public ConcurrentList<BufferLayer> Layers { get; } = [];
    public BufferLayer MergedLayer
    {
        get
        {
            var layer = new BufferLayer(0, Size, DefaultForeground, DefaultBackground);
            for (var y = 0; y < Height; y++)
            {
                var row = layer[y].Span;
                for (var x = 0; x < Width; x++)
                {
                    row[x] = Cell(x, y);
                }
            }
            return layer;
        }
    }

    public void Clear() => _layers.Clear();

    public void Resize(Size newSize)
    {
        Size = newSize;
        _layers.Clear();
    }

    public void Write(int zOrder, Position position, Size size, ConxelString conxelString)
    {
        var y = (int)position.Y;
        if (y < 0 || y >= Height)
        {
            return;
        }

        if (!_layers.TryGetValue(zOrder, out var layer))
        {
            layer = new Conxel?[Width, Height];
            _layers[zOrder] = layer;
        }

        var count = Math.Min(conxelString.Count, (int)size.Width);
        for (var i = 0; i < count; i++)
        {
            var x = (int)position.X + i;
            if (x >= Width)
            {
                break;
            }
            layer[x, y] = conxelString[i];
        }
    }

    public void SetCursorPosition(Position position, bool isVisible)
    {
        Cursor.CursorPosition = position;
        Cursor.IsCursorVisible = isVisible;
    }

    public void Merge()
    {
    }

    public IEnumerable<int> UpdateLayers(int maxZOrder) => [];
    #endregion

    #region Reading
    /// <summary>The composed cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public Conxel Cell(int x, int y)
    {
        foreach (var layer in _layers.Values.Reverse())
        {
            if (layer[x, y] is { } conxel)
            {
                return conxel;
            }
        }
        return new Conxel(' ', DefaultForeground, DefaultBackground);
    }

    /// <summary>True when any layer wrote the cell — i.e. some control took responsibility for it.</summary>
    public bool IsWritten(int x, int y) => _layers.Values.Any(layer => layer[x, y] is not null);

    public char CharAt(int x, int y) => Cell(x, y).Char;
    public Color ForegroundAt(int x, int y) => Cell(x, y).ForegroundColor;
    public Color BackgroundAt(int x, int y) => Cell(x, y).BackgroundColor;

    /// <summary>Row <paramref name="y"/>, full width, trailing blanks kept.</summary>
    /// <summary>
    /// The text of row <paramref name="y"/> as a person reads it. The second column of a wide character is left out, so
    /// a column index is not a character index on a row that has wide characters before it.
    /// </summary>
    public string Line(int y)
    {
        var builder = new StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            if (CharAt(x, y) != iConsole.Display.TextWidth.Continuation)
            {
                builder.Append(CharAt(x, y));
            }
        }
        return builder.ToString();
    }

    /// <summary><paramref name="width"/> characters of row <paramref name="y"/> from column <paramref name="x"/>.</summary>
    public string Text(int x, int y, int width) => Line(y).Substring(x, Math.Min(width, Width - x));

    /// <summary>Every row with trailing blanks removed.</summary>
    public string[] Lines => Enumerable.Range(0, Height).Select(y => Line(y).TrimEnd()).ToArray();

    /// <summary>The screen as text, trailing blanks and trailing empty rows removed.</summary>
    public override string ToString()
    {
        var lines = Lines.ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The first cell where <paramref name="text"/> starts, scanning rows top to bottom.</summary>
    public Position? Find(string text)
    {
        for (var y = 0; y < Height; y++)
        {
            var x = Line(y).IndexOf(text, StringComparison.Ordinal);
            if (x >= 0)
            {
                return new Position(x, y);
            }
        }
        return null;
    }

    public bool Contains(string text) => Find(text) is not null;

    /// <summary>The colours of the first cell of <paramref name="text"/>; fails when it is not on screen.</summary>
    public Conxel CellOf(string text)
    {
        var position = Find(text);
        Assert.IsNotNull(position, $"'{text}' is not on screen:{Environment.NewLine}{this}");
        return Cell((int)position.X, (int)position.Y);
    }
    #endregion

    #region Assertions
    /// <summary>Asserts the screen reads exactly <paramref name="expectedLines"/> (trailing blanks ignored).</summary>
    public void AssertLines(params string[] expectedLines)
    {
        var expected = string.Join(Environment.NewLine, expectedLines.Select(l => l.TrimEnd())).TrimEnd();
        StringAssert.AreEqual(expected, ToString());
    }

    /// <summary>Asserts every cell of <paramref name="text"/> has the given colours.</summary>
    public void AssertColors(string text, Color foreground, Color background)
    {
        var position = Find(text);
        Assert.IsNotNull(position, $"'{text}' is not on screen:{Environment.NewLine}{this}");
        AssertColors((int)position.X, (int)position.Y, text.Length, foreground, background);
    }

    /// <summary>Asserts <paramref name="width"/> cells from (<paramref name="x"/>, <paramref name="y"/>) have the given colours.</summary>
    public void AssertColors(int x, int y, int width, Color foreground, Color background)
    {
        for (var i = x; i < x + width; i++)
        {
            Assert.AreEqual(foreground, ForegroundAt(i, y), $"Foreground at ({i},{y}) in '{Line(y).TrimEnd()}'");
            Assert.AreEqual(background, BackgroundAt(i, y), $"Background at ({i},{y}) in '{Line(y).TrimEnd()}'");
        }
    }

    /// <summary>
    /// Asserts <paramref name="control"/> painted every cell of its rectangle. A control that leaves
    /// a cell unwritten shows whatever the previous frame left there.
    /// </summary>
    public void AssertFillsBounds(IControl control)
    {
        var position = control.AbsolutePosition;
        var size = control.ViewSize;
        for (var y = (int)position.Y; y < Math.Min(Height, (int)(position.Y + size.Height)); y++)
        {
            for (var x = (int)position.X; x < Math.Min(Width, (int)(position.X + size.Width)); x++)
            {
                Assert.IsTrue(IsWritten(x, y), $"Cell ({x},{y}) was not painted by {control.GetType().Name}:{Environment.NewLine}{this}");
            }
        }
    }
    #endregion

    /// <summary>Draws <paramref name="control"/> into a fresh buffer of the given size.</summary>
    public static ScreenBuffer Render(IControl control, int width, int height)
    {
        var screen = new ScreenBuffer(width, height);
        control.Draw(screen);
        return screen;
    }
}
