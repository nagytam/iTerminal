using iConsole.Data;
using iConsole.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace iConsole.Test.Core.Automation;

/// <summary>
/// One frame of a <see cref="VirtualConsoleDriver"/>: characters, colours and style flags of every
/// cell, plus the cursor. Immutable, so a test can hold on to it while the app keeps rendering.
/// </summary>
public sealed class ConsoleSnapshot
{
    private readonly Conxel[,] _cells;

    internal ConsoleSnapshot(Conxel[,] cells, Position cursorPosition, bool isCursorVisible, long version)
    {
        _cells = cells;
        CursorPosition = cursorPosition;
        IsCursorVisible = isCursorVisible;
        Version = version;
    }

    public int Width => _cells.GetLength(0);
    public int Height => _cells.GetLength(1);
    public Position CursorPosition { get; }
    public bool IsCursorVisible { get; }

    /// <summary>The driver's <see cref="VirtualConsoleDriver.Version"/> when this frame was taken.</summary>
    public long Version { get; }

    public Conxel Cell(int x, int y) => _cells[x, y];
    public char CharAt(int x, int y) => _cells[x, y].Char;
    public Color ForegroundAt(int x, int y) => _cells[x, y].ForegroundColor;
    public Color BackgroundAt(int x, int y) => _cells[x, y].BackgroundColor;

    /// <summary>Row <paramref name="y"/>, full width, trailing blanks kept.</summary>
    public string Line(int y)
    {
        var builder = new StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            var c = _cells[x, y].Char;
            builder.Append(c == '\0' ? ' ' : c);
        }
        return builder.ToString();
    }

    /// <summary>Every row with trailing blanks removed.</summary>
    public IReadOnlyList<string> Lines => Enumerable.Range(0, Height).Select(y => Line(y).TrimEnd()).ToArray();

    /// <summary><paramref name="width"/> characters of row <paramref name="y"/> from column <paramref name="x"/>.</summary>
    public string Text(int x, int y, int width) => Line(y).Substring(x, Math.Clamp(width, 0, Width - x));

    /// <summary>The rows of a rectangle — e.g. one panel or one dialog — trailing blanks removed.</summary>
    public IReadOnlyList<string> Region(int x, int y, int width, int height) =>
        Enumerable.Range(y, Math.Clamp(height, 0, Height - y))
            .Select(row => Text(x, row, width).TrimEnd())
            .ToArray();

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

    /// <summary>Every cell where <paramref name="text"/> starts.</summary>
    public IReadOnlyList<Position> FindAll(string text)
    {
        List<Position> positions = [];
        for (var y = 0; y < Height; y++)
        {
            var line = Line(y);
            for (var x = line.IndexOf(text, StringComparison.Ordinal); x >= 0; x = line.IndexOf(text, x + 1, StringComparison.Ordinal))
            {
                positions.Add(new Position(x, y));
            }
        }
        return positions;
    }

    public bool Contains(string text) => Find(text) is not null;

    /// <summary>The row <paramref name="text"/> is first found on, or -1.</summary>
    public int RowOf(string text) => Find(text) is { } position ? (int)position.Y : -1;

    /// <summary>The first cell of <paramref name="text"/>; fails the test when it is not on screen.</summary>
    public Conxel CellOf(string text)
    {
        var position = Find(text) ?? throw new AssertFailedException($"'{text}' is not on screen:{Environment.NewLine}{this}");
        return Cell((int)position.X, (int)position.Y);
    }

    /// <summary>True when every cell — character, colours and style — matches <paramref name="other"/>.</summary>
    public bool HasSameContent(ConsoleSnapshot other)
    {
        if (other.Width != Width || other.Height != Height)
        {
            return false;
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (_cells[x, y] != other._cells[x, y])
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>True when the screen has nothing but blanks — what a broken first frame looks like.</summary>
    public bool IsBlank => Lines.All(line => line.Length == 0);

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

    /// <summary>The screen framed with row numbers, for failure messages.</summary>
    public string Dump()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"screen {Width}x{Height}, cursor {(IsCursorVisible ? $"({CursorPosition.X},{CursorPosition.Y})" : "hidden")}:");
        for (var y = 0; y < Height; y++)
        {
            builder.Append(y.ToString("D2")).Append('|').Append(Line(y)).AppendLine("|");
        }
        return builder.ToString();
    }
}
