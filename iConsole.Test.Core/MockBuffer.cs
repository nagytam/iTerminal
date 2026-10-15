using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;

namespace iConsole.Test.Core;

public class MockBuffer(Size size) : IBuffer
{
    public Dictionary<int, string> Content { get; private set; } = [];
    public Conxel[] this[long row] => throw new NotImplementedException();
    public ConcurrentList<BufferLayer> Layers { get; private set; } = [];
    public Cursor Cursor { get; set; } = new Cursor(size);
    public BufferLayer MergedLayer => Layers[0];
    public Size Size { get; set; } = size;
    public Lock SyncLock { get; } = new();
    public bool IsLocked { get; set; }
    public bool IsDirty { get; set; }
    public bool IsCursorVisible { get; set; }
    public Position CursorPosition { get; set; } = new Position(0, 0);

    public string ContentAsString
    {
        get
        {
            var contentAsString = string.Empty;
            foreach (var item in Content.OrderBy(kvp => kvp.Key))
            {
                contentAsString += item.Value + Environment.NewLine;
            }
            return contentAsString;
        }
    }

    public void Clear()
    {
        Content.Clear();
    }

    public void Resize(Size newSize)
    {
        Size = newSize;
    }

    public void Write(Position absolutePosition, Size size, string text, Color foreground, Color background)
    {
        string prefix;
        string postfix = string.Empty;

        if (!Content.ContainsKey((int)absolutePosition.Y))
        {
            for (int y = 0; y <= absolutePosition.Y; y++)
            {
                if (!Content.ContainsKey(y))
                {
                    Content[y] = string.Empty;
                }
            }
        }

        var content = Content[(int)absolutePosition.Y];
        if (content.Length >= absolutePosition.X)
        {
            prefix = content[..(int)(absolutePosition.X)];
        }
        else
        {
            prefix = content.PadRight((int)absolutePosition.X);
            content = prefix;
        }
        var index = (int)prefix.Length + text.Length;
        if (content.Length > index && index < size.Width)
        {
            postfix = content[(int)(index)..((int)Math.Min(size.Width, content.Length))];
        }
        content = prefix + text + postfix;
        content = content[..Math.Min(content.Length, (int)Size.Width)];
        Content[(int)absolutePosition.Y] = content;
    }

    public void Write(int zOrder, Position absolutePosition, Size size, ConxelString conxelString)
    {
        string prefix;
        string postfix = string.Empty;

        if (!Content.ContainsKey((int)absolutePosition.Y))
        {
            for (int y = 0; y <= absolutePosition.Y; y++)
            {
                if (!Content.ContainsKey(y))
                {
                    Content[y] = string.Empty;
                }
            }
        }

        var content = Content[(int)absolutePosition.Y];
        if (content.Length >= absolutePosition.X)
        {
            prefix = content[..(int)(absolutePosition.X)];
        }
        else
        {
            prefix = content.PadRight((int)absolutePosition.X);
            content = prefix;
        }
        var index = (int)prefix.Length + conxelString.Count;
        if (content.Length > index && index < size.Width)
        {
            postfix = content[index..(int)Math.Min(size.Width, content.Length)];
        }
        content = prefix + conxelString.Text + postfix;
        content = content[..Math.Min(content.Length, (int)Size.Width)];
        Content[(int)absolutePosition.Y] = content;
    }

    public void SetCursorPosition(Position position, bool isVisible)
    {
        CursorPosition = position;
        IsCursorVisible = isVisible;
        Cursor.CursorPosition = position;
        Cursor.IsCursorVisible = isVisible;
    }

    public void DrawBorder(int zOrder, Position position, Size size, string header, string footer, ColorScheme colorScheme, VisualState visualState)
    {
        throw new NotImplementedException();
    }

    public void Merge()
    {
        if (!Layers.Any())
        {
            Layers.Add(new BufferLayer(0, Size, Color.White, Color.Black));
        }

        var mergedLayer = Layers[0];
        mergedLayer.Resize(Size);
        mergedLayer.Clear();

        foreach (var row in Content)
        {
            var y = row.Key;
            if (y < 0 || y >= Size.Height)
            {
                continue;
            }

            var text = row.Value ?? string.Empty;
            var line = new ConxelString(text, Color.White, Color.Black);
            mergedLayer.Write(new Position(0, y), new Size(Math.Min(text.Length, (int)Size.Width), 1), line);
        }
    }

    public IEnumerable<int> UpdateLayers(int zOrder)
    {
        if (!Layers.Any())
        {
            Layers.Add(new BufferLayer(0, Size, Color.White, Color.Black));
        }
        return [];
    }
}