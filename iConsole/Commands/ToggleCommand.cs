using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Commands;

/// <summary>Flips a focused <see cref="ToggleButton"/>'s <see cref="ToggleButton.IsOn"/> state.</summary>
public class ToggleCommand : Command
{
    public override Key? Key { get; init; }

    public ToggleCommand(Key key)
    {
        Key = key;
    }

    public override bool CanExecute()
    {
        return !IsReadOnlyControl;
    }

    public override Task ExecuteAsync(object? args)
    {
        if (Control is ToggleButton toggleButton)
        {
            toggleButton.IsOn = !toggleButton.IsOn;
        }
        return Task.CompletedTask;
    }
}
