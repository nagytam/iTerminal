using iConsole.Commands;

namespace iTerminal.Core.Dialogs.Calculator;

internal sealed class CalculatorActionCommand : Command
{
    private readonly Func<string> _title;
    private readonly Action _action;
    private readonly Func<bool> _canExecute;

    public CalculatorActionCommand(Func<string> title, Action action, Func<bool>? canExecute = null)
    {
        _title = title;
        _action = action;
        _canExecute = canExecute ?? (() => true);
    }

    public override string Title => _title();

    public override bool CanExecute() => _canExecute();

    public override Task ExecuteAsync(object? args)
    {
        _action();
        return Task.CompletedTask;
    }
}
