using iConsole.Data;

namespace iConsole.Inputs;

/// <summary>
/// Sends keys and mouse events into the application as if a person had pressed or moved them, through the same routing
/// a real keyboard and mouse use. A test drives the application with this instead of a terminal.
/// </summary>
/// <remarks>
/// Each task completes once the event has been handled and says whether anything handled it. Callers must not await
/// it from the thread that dispatches input, or it never completes.
/// </remarks>
public interface IInputInjector
{
    /// <summary>Sends a key press; the task says whether anything handled it.</summary>
    Task<bool> SendKeyAsync(KeyEvent keyEvent);

    /// <summary>Sends a mouse event; the task says whether anything handled it.</summary>
    Task<bool> SendMouseAsync(MouseEvent mouseEvent);
}
