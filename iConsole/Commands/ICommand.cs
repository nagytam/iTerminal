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

    bool CanExecute();
    Task<bool> HandleKeyEventAsync(KeyEvent keyEvent);
    Task<bool> RunAsync(object? args);
}