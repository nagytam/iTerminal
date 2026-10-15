using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.Model;

public class TerminalModel(
    TaskManager taskManager,
    IContentModelService contentModelService
    ) 
    : ITerminalModel, INotifyPropertyChanged
{
    private ITerminalCommand _command = new TerminalCommand();
    private readonly TaskManager _taskManager = taskManager;

    public ITerminalCommand Command 
    { 
        get
        {
            return _command;
        }
        set
        {
            if (_command != value)
            {
                _command = value;
                OnPropertyChanged(nameof(Command));
            }
        }
    } 

    public ConcurrentList<ITerminalCommand> History { get; set; } = [];

    /// <summary>True while the current command is running.</summary>
    public bool IsRunning => Command.CommandTask is not null;

    /// <summary>
    /// Starts the current command and returns at once — this is called from the Enter key handler.
    ///
    /// <para>It used to hold <see cref="ITerminalCommand.CommandTask"/> to the value
    /// <see cref="TaskManager.Add"/> returns, which is already complete, and then wait on the
    /// command's inner task. Whether that wait blocked depended on a race with the worker: either
    /// the key handler froze the whole app until the shell command finished, or the command was
    /// marked idle before it had even started, so the prompt never showed it running and a second
    /// Enter started it again. <see cref="ITerminalCommand.CommandTask"/> is now the command's own
    /// completion, cleared when it has actually finished.</para>
    /// </summary>
    public void StartCommand()
    {
        var command = Command;
        if (command.CommandTask is not null)
        {
            return;
        }

        command.Output = null;
        command.Error = null;

        var contentModel = contentModelService.GetLastContentModel();
        var executeCommandTask = new ExecuteCommandTask()
        {
            ContentModel = contentModel,
            TerminalModel = this
        };

        command.CommandTask = executeCommandTask.Completion;
        OnPropertyChanged(nameof(Command));

        _ = executeCommandTask.Completion.ContinueWith(_ =>
        {
            command.CommandTask = null;
            OnPropertyChanged(nameof(Command));
        }, TaskScheduler.Default);

        _ = _taskManager.Add(executeCommandTask);
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal void AddCommandHistory(ITerminalCommand command)
    {
        History.Add(command);
        OnPropertyChanged(nameof(History));
        Command = new TerminalCommand();
    }
    #endregion
}
