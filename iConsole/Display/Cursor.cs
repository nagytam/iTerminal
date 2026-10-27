using iConsole.Data;

namespace iConsole.Display;

public record Cursor(Size size)
{
    public Size Size { get; set; } = size;
    public bool IsCursorVisible { get; set; } = false;

    /// <summary>The shape the cursor is drawn with while it is visible.</summary>
    public CursorShape Shape { get; set; } = CursorShape.Default;

    private Position? _cursorPosition = null;
    public Position? CursorPosition
    {
        get
        {
            if (IsCursorVisible)
            {
                return _cursorPosition;
            }
            else
            {
                return null;
            }
        }
        set
        {
            if (_cursorPosition != value)
            {
                if (value != null)
                {
                    var x = Math.Max(Math.Min(value.X, Size.Width - 1), 0);
                    var y = Math.Max(Math.Min(value.Y, Size.Height - 1), 0);
                    _cursorPosition = new Position(x, y);
                }
                else
                {
                    _cursorPosition = null;
                }
            }
        }
    }
}
