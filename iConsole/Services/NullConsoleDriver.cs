using iConsole.Data;
using iConsole.Display;

namespace iConsole.Services;

public sealed class NullConsoleDriver : Drivers.IConsoleDriver
{
    public static readonly NullConsoleDriver Instance = new();

    private NullConsoleDriver() { }

    public bool IsCursorVisible { get; set; }
    public Color ForegroundColor { get; set; }
    public Color BackgroundColor { get; set; }
    public Size WindowSize => new(0, 0);
    public string Title { get; set; } = string.Empty;

    public void Beep() { }
    public void Clear() { }
    public Position GetCursorPosition() => new(0, 0);
    public void RefreshWindowSize() { }
    public void SetCursorPosition(int x, int y) { }
    public void Write(int x, int y, Conxel conxel) { }
    public void Write(int x, int y, ConxelString s) { }
}
