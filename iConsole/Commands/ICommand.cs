using iConsole.Controls;
using iConsole.Data;
using iConsole.Diagnostics;

namespace iConsole.Commands;

public interface ICommand
{
    string Title { get; }
    string Description { get; }
    string KeyDisplayText { get; }
    IControl? Control { get; set; }
    Key? Key { get; }
    /// <summary>The key the command answers to now: <see cref="Key"/>, unless the user's key map changes it.</summary>
    Key? ActiveKey { get; }
    ICommandLogger CommandLogger { get; set; }

    /// <summary>
    /// Whether the command can run now. It is called on the render thread each time a button bound to the
    /// command is drawn, and on the input thread before every run, so answer from memory: no I/O, no process
    /// and no wait. A check that needs the disk or a server belongs in <see cref="RunAsync"/>, or in a model
    /// that already holds the answer (a listing).
    /// </summary>
    bool CanExecute();
    Task<bool> HandleKeyEventAsync(KeyEvent keyEvent);
    Task<bool> RunAsync(object? args);
}