using iConsole.Data;
using iConsole.Display;

namespace iConsole.Drivers;

/// <summary>
/// The terminal the renderer writes to. As an <see cref="Services.IConsoleHandoverParticipant"/> it
/// puts the screen back to plain (default colours, visible cursor, cleared) while a child process
/// has the terminal, and takes it over again afterwards; a driver with nothing to undo inherits
/// the no-ops.
/// </summary>
public interface IConsoleDriver : Services.IConsoleHandoverParticipant
{
    bool IsCursorVisible { get; set; }

    /// <summary>The shape of the cursor; a driver on a terminal that cannot change it keeps <see cref="CursorShape.Default"/>.</summary>
    CursorShape CursorShape
    {
        get => CursorShape.Default;
        set
        {
        }
    }

    Color ForegroundColor { get; set; }
    Color BackgroundColor { get; set; }
    Size WindowSize { get; }
    string Title { get; set; }

    void Beep();
    void Clear();
    Position GetCursorPosition();
    void RefreshWindowSize();
    void SetCursorPosition(int x, int y);
    void Write(int x, int y, Conxel conxel);
    void Write(int x, int y, ConxelString s);

    /// <summary>Called before the writes of one frame. A driver on a terminal that knows synchronized output starts holding it.</summary>
    void BeginFrame()
    {
    }

    /// <summary>Called after the writes of one frame, whatever happened in between.</summary>
    void EndFrame()
    {
    }

    /// <summary>Draws a Sixel picture (the complete device control string) with its top left corner at the cell. A driver that cannot draw pictures ignores it.</summary>
    void WriteGraphics(int x, int y, string sequence)
    {
    }
}