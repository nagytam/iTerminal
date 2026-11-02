using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace iConsole.Buffers;

public class BufferLayer
{
    public int ZOrder { get; private set; }
    public Size Size { get; private set; }
    public Memory<Conxel>[] Content { get; private set; }
    public Memory<Conxel> this[long row] 
    { 
        get 
        { 
            return Content[row]; 
        } 
        set
        {
            Content[row] = value;
        }
    }
    public Color DefaultForegroundColor { get; set; }
    public Color DefaultBackgroundColor { get; set; }
    public Rectangle? BoundRect { get; set; } = null;

    // The cells written on each row since the layer was last cleared, as [left, right). A layer is
    // composited row by row over just these cells, so two overlays that share a layer (a dialog and
    // an open menu, say) do not blank the panels between them, as compositing the whole bounding
    // rectangle did. Rows nothing was written to are left alone.
    private int[] _rowLeft = [];
    private int[] _rowRight = [];
    private bool _hasRowExtents;

    private void ResetRowExtents()
    {
        _rowLeft = new int[Size.Height];
        _rowRight = new int[Size.Height];
        Array.Fill(_rowLeft, int.MaxValue);
        _hasRowExtents = false;
    }

    /// <summary>The written span of row <paramref name="y"/> as [left, right); empty when nothing was written there.</summary>
    public (int Left, int Right) GetRowExtent(int y) =>
        y >= 0 && y < _rowRight.Length && _rowLeft[y] < _rowRight[y] ? (_rowLeft[y], _rowRight[y]) : (0, 0);

    public BufferLayer(int zOrder, Size size, Color foreground, Color background)
    {
        ZOrder = zOrder;
        Size = size;
        DefaultForegroundColor = foreground;
        DefaultBackgroundColor = background;
        Content = new Memory<Conxel>[Size.Height];
        InitContent();
        if (ZOrder == 0)
        {
            BoundRect = new Rectangle(new Position(0, 0), size);
        }
    }

    protected void InitContent()
    {
        ResetRowExtents();
        for (int y = 0; y < Size.Height; y++)
        {
            var defaultLine = GetDefaultLine();
            Content[y] = new Memory<Conxel>(defaultLine);
        }
    }

    private Conxel[] GetDefaultLine()
    {
        var line = new Conxel[Size.Width];
        Array.Fill(line, new Conxel(' ', DefaultForegroundColor, DefaultBackgroundColor));
        return line;
    }

    public void Resize(Size size)
    {
        if (Size != size)
        {
            Size = size;
            Content = new Memory<Conxel>[Size.Height];
            BoundRect = null;
            InitContent();
        }
    }

    public void Clear()
    {
        var defaultChar = new Conxel(' ', DefaultForegroundColor, DefaultBackgroundColor);
        for (int y = 0; y < Size.Height; y++)
        {
            Content[y].Span.Fill(defaultChar);
        }
        BoundRect = null;
        ResetRowExtents();
    }

    public void Clear(Conxel fill)
    {
        for (int y = 0; y < Size.Height; y++)
        {
            Content[y].Span.Fill(fill);
        }
        BoundRect = null;
        ResetRowExtents();
    }

    public void Write(Position position, Size size, ConxelString conxelString)
    {
        if (conxelString.Any())
        {
            var consoleY = position.Y;
            if (consoleY < Math.Min(Size.Height, consoleY + size.Height) 
                && position.X < Size.Width)
            {
                var sizeX = MathHelper.Min(
                                conxelString.Count, 
                                (int)size.Width, 
                                (int)(Size.Width - position.X));
                // The bounds grow by what is written — one row, sizeX cells — not by the size the
                // caller passed. Most controls pass their whole ViewSize for every row they draw, and
                // a layer is composited over the ones below across its whole bound rectangle, blanks
                // included: so a dialog's list blanked the rows below the dialog (the status lines
                // under every dialog on a small screen), and an open menu blanked the panels beside it.
                if (sizeX > 0)
                {
                    var written = new Rectangle((int)position.X, (int)consoleY, sizeX, 1);
                    if (BoundRect is null)
                    {
                        BoundRect = written;
                    }
                    else
                    {
                        BoundRect.Include(written);
                    }
                    var row = (int)consoleY;
                    if (row < _rowLeft.Length)
                    {
                        _rowLeft[row] = Math.Min(_rowLeft[row], (int)position.X);
                        _rowRight[row] = Math.Max(_rowRight[row], (int)position.X + sizeX);
                        _hasRowExtents = true;
                    }                }
                Span<Conxel> sourceSpan = CollectionsMarshal.AsSpan(conxelString.String)[..sizeX];
                var destinationLineSpan = this[consoleY].Span;
                if (position.X < destinationLineSpan.Length)
                {
                    var destinationSpan = destinationLineSpan[(int)position.X..];
                    if (destinationSpan.Length > 0 && sourceSpan.Length <= destinationSpan.Length)
                    {
                        sourceSpan.CopyTo(destinationSpan);
                    }
                    else
                    {
                        // Debugger.Break();
                    }
                }
            }
        }
    }

    public string AsString()
    {
        StringBuilder stringBuilder = new();
        foreach (var line in Content)
        {
            foreach (var c in line.ToArray())
            {
                stringBuilder.Append(c.Char);
            }
            stringBuilder.AppendLine();
        }
        return stringBuilder.ToString();
    }

    /// <summary>
    /// Replaces each half of a wide character that lost its partner with a space. Layers are composited cell by cell,
    /// so a narrow character drawn over one column of a wide character leaves the other column behind; drawn
    /// unrepaired, the terminal would show a character that the cell model does not have. Run once on the merged
    /// layer, after compositing.
    /// </summary>
    public void RepairWideCharacters()
    {
        for (int y = 0; y < Content.Length; y++)
        {
            var row = Content[y].Span;
            for (int x = 0; x < row.Length; x++)
            {
                var c = row[x].Char;
                bool isValid = c switch
                {
                    TextWidth.Continuation => x > 0 && IsLeadingWide(row[x - 1].Char),
                    _ when char.IsHighSurrogate(c) => x + 1 < row.Length && char.IsLowSurrogate(row[x + 1].Char),
                    _ when char.IsLowSurrogate(c) => x > 0 && char.IsHighSurrogate(row[x - 1].Char),
                    _ when TextWidth.Width(c) == 2 => x + 1 < row.Length && row[x + 1].Char == TextWidth.Continuation,
                    _ => true,
                };
                if (!isValid)
                {
                    var cell = row[x];
                    cell.Char = ' ';
                    row[x] = cell;
                }
            }
        }
    }

    private static bool IsLeadingWide(char c) =>
        c != TextWidth.Continuation && !char.IsSurrogate(c) && TextWidth.Width(c) == 2;

    public void CopyLayer(BufferLayer mergedLayer)
    {
        var maxY = Math.Min(Size.Height, Content.Length);
        for (int y = 0; y < maxY; y++)
        {
            var sourceSpan = this[y].Span;
            var targetSpan = mergedLayer[y].Span;
            sourceSpan.CopyTo(targetSpan);
        }
    }

    public void Merge(BufferLayer layer, Rectangle boundRect)
    {
        Debug.Assert(layer != null && boundRect is not null);
        for (int y = boundRect.Top; y < boundRect.Bottom; y++)
        {
            if (y < layer.Content.Length && y < Content.Length)
            {
                var left = boundRect.Left;
                var right = boundRect.Right;
                if (layer._hasRowExtents)
                {
                    var (rowLeft, rowRight) = layer.GetRowExtent(y);
                    left = Math.Max(left, rowLeft);
                    right = Math.Min(right, rowRight);
                }
                left = Math.Max(0, left);
                right = Math.Min(right, Math.Min((int)layer.Size.Width, this[y].Length));
                if (right <= left)
                {
                    continue;
                }
                var sourceSpan = layer[y].Span[left..right];
                var targetSpan = this[y].Span[left..right];
                sourceSpan.CopyTo(targetSpan);
            }
        }
    }
}
