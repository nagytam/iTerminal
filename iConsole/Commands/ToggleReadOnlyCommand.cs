using iConsole.Controls;
using iConsole.Data;
using System.ComponentModel;

namespace iConsole.Commands;

public class ToggleReadOnlyCommand : Command
{
    private readonly IReadOnly _readOnly;

    public ToggleReadOnlyCommand(IReadOnly readOnly)
    {
        _readOnly = readOnly;
        readOnly.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Control?.Refresh();
    }

    public override string Title
    {
        get
        {
            if (_readOnly.IsReadOnly)
            {
                return "F6-Writable";
            }
            else
            {
                return "F6-ReadOnly";
            }
        }
    }

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F6, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        if (_readOnly is not null)
        {
            _readOnly.IsReadOnly = !_readOnly.IsReadOnly;
        }
        return Task.CompletedTask;
    }
}
