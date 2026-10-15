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
}