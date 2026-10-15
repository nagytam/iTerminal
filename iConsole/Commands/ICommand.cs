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
    ICommandLogger CommandLogger { get; set; }

    bool CanExecute();
    Task<bool> HandleKeyEventAsync(KeyEvent keyEvent);
    Task<bool> RunAsync(object? args);
}