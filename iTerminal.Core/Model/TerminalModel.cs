using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Tasks;
using iTerminal.Core.Services.Shell;
using iTerminal.Core.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.Model;

/// <param name="shellSessions">
/// The long-lived shell the commands run in, or null for the one-shot path (every local command then runs as it
/// always has). Registered as a singleton, so the command log and Enter on a program share it.
/// </param>
public class TerminalModel(
    TaskManager taskManager,
    IContentModelService contentModelService,
    ILocalShellSessionService? shellSessions = null
    )
    : ITerminalModel, INotifyPropertyChanged
{
    private ITerminalCommand _command = new TerminalCommand();
    private readonly TaskManager _taskManager = taskManager;

    /// <summary>The shell session the command log runs in, or null when there is none.</summary>
    public ILocalShellSessionService? ShellSessions { get; } = shellSessions;

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

        // A plain "cd" moves the panel: the shell it would run in is gone when the command ends, so
        // run as a command it changed nothing the user could see.
        if (TryChangeDirectory())
        {
            return;
        }

        command.Output = null;
        command.Error = null;

        // "shell" is the app's own line, like "cd": it changes the session, so it is not sent to the shell.
        var shellArgument = ShellLineArgument(command.Command);
        var contentModel = contentModelService.GetLastContentModel();
        var executeCommandTask = new ExecuteCommandTask()
        {
            ContentModel = shellArgument is null ? contentModel : null,
            TerminalModel = this,
            Builtin = shellArgument is null ? null : () => RunShellLineAsync(command, shellArgument),
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

    #region Command line editing
    private int _recallIndex = -1;
    private string _draft = string.Empty;
    private string? _previousDirectory;

    /// <summary>The characters that make a "cd ..." line more than a change of directory.</summary>
    private static readonly char[] ShellOperators = ['&', ';', '|', '<', '>', '`', '$', '(', ')'];

    /// <summary>
    /// A file or folder name as it is written on the command line: in double quotes when it holds a
    /// blank, so what is inserted is one word to every shell.
    /// </summary>
    public static string QuoteName(string name) =>
        name.Length > 0 && name.IndexOfAny([' ', '	']) >= 0 && !name.Contains('"')
            ? "\"" + name + "\""
            : name;

    /// <summary>
    /// Appends <paramref name="text"/> to the command being typed, a blank in between when the line
    /// does not end in one — what Alt+Enter does with the file under the cursor. Nothing happens
    /// while a command is running: that line is the running command.
    /// </summary>
    public bool InsertText(string text)
    {
        if (string.IsNullOrEmpty(text) || IsRunning)
        {
            return false;
        }

        var current = Command.Command ?? string.Empty;
        Command.Command = current.Length == 0 || char.IsWhiteSpace(current[^1])
            ? current + text
            : current + " " + text;
        _recallIndex = -1;
        OnPropertyChanged(nameof(Command));
        return true;
    }

    /// <summary>
    /// Puts the previous command of the history on the line (Alt+P). The line being typed is kept
    /// and comes back when <see cref="RecallNext"/> runs past the newest command.
    /// </summary>
    public bool RecallPrevious()
    {
        if (IsRunning || History.Count == 0)
        {
            return false;
        }

        if (_recallIndex < 0)
        {
            _draft = Command.Command ?? string.Empty;
            _recallIndex = History.Count;
        }

        if (_recallIndex == 0)
        {
            return true;
        }

        _recallIndex--;
        Command.Command = History[_recallIndex].Command;
        OnPropertyChanged(nameof(Command));
        return true;
    }

    /// <summary>Puts the next command of the history on the line (Alt+N), then the line that was being typed.</summary>
    public bool RecallNext()
    {
        if (IsRunning || _recallIndex < 0)
        {
            return false;
        }

        _recallIndex++;
        if (_recallIndex >= History.Count)
        {
            _recallIndex = -1;
            Command.Command = _draft;
        }
        else
        {
            Command.Command = History[_recallIndex].Command;
        }

        OnPropertyChanged(nameof(Command));
        return true;
    }

    /// <summary>
    /// Handles <c>cd</c>, <c>cd &lt;folder&gt;</c>, <c>cd ~</c> and <c>cd -</c> typed on the command
    /// line by pointing the panel at the folder, as mc does, and records it in the history.
    /// Returns false when the line is anything else — another program, a <c>cd</c> chained to
    /// something with a shell operator, or a panel that is not on the local disk, where the
    /// command goes to the drive's own shell as before.
    /// </summary>
    internal bool TryChangeDirectory()
    {
        var text = (Command.Command ?? string.Empty).Trim();
        if (!(text == "cd" || text.StartsWith("cd ", StringComparison.Ordinal) || text.StartsWith("cd	", StringComparison.Ordinal))
            || text.IndexOfAny(ShellOperators) >= 0)
        {
            return false;
        }

        var model = contentModelService.GetLastContentModel();
        if (model?.Url is not { ContainerType: ContainerType.FileSystem } current)
        {
            return false;
        }

        var argument = text[2..].Trim();
        if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"')
        {
            argument = argument[1..^1];
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var currentFolder = Path.TrimEndingDirectorySeparator(current.Path);
        string target;
        if (argument.Length == 0 || argument == "~")
        {
            target = home;
        }
        else if (argument.StartsWith("~/", StringComparison.Ordinal) || argument.StartsWith("~\\", StringComparison.Ordinal))
        {
            target = Path.Combine(home, argument[2..]);
        }
        else if (argument == "-")
        {
            target = _previousDirectory ?? currentFolder;
        }
        else
        {
            target = Path.GetFullPath(Path.Combine(currentFolder + Path.DirectorySeparatorChar, argument));
        }

        var record = Command;
        record.WorkingDirectoryUrl = current;
        record.StartTime = DateTime.Now;
        record.ExitTime = record.StartTime;
        if (Directory.Exists(target))
        {
            _previousDirectory = currentFolder;
            record.ExitCode = 0;
            record.Output = null;
            _ = contentModelService.SetUrlAsync(model.Name, new Url(Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar));
        }
        else
        {
            record.ExitCode = 1;
            record.Error = $"cd: {argument}: no such directory";
        }

        AddCommandHistory(record);
        return true;
    }

    /// <summary>
    /// The argument of a <c>shell</c> line: "" for a bare <c>shell</c> (which lists the shells), the name after it
    /// otherwise, or null when the line is not a shell line. A line joined to something with a shell operator is not
    /// one, so it is sent to the shell as typed.
    /// </summary>
    internal static string? ShellLineArgument(string? commandText)
    {
        var text = (commandText ?? string.Empty).Trim();
        if (text == "shell")
        {
            return string.Empty;
        }

        if (!(text.StartsWith("shell ", StringComparison.Ordinal) || text.StartsWith("shell	", StringComparison.Ordinal))
            || text.IndexOfAny(ShellOperators) >= 0)
        {
            return null;
        }

        return text[5..].Trim();
    }

    /// <summary>
    /// Runs a <c>shell</c> line on the background task (finding the shells touches the PATH, and switching ends a
    /// process): lists the shells, or switches to the named one. Refused while a command runs in the session.
    /// </summary>
    internal Task RunShellLineAsync(ITerminalCommand command, string argument)
    {
        command.StartTime = DateTime.Now;
        var sessions = ShellSessions;
        var available = sessions is { IsAvailable: true } ? sessions.Available() : [];
        var names = string.Join(", ", available.Select(ShellKinds.Name));

        if (sessions is null || available.Count == 0)
        {
            command.Error = UiText.Get("Terminal.ShellNone", "No shell session is available here: sh or bash must be on the PATH. Commands run one at a time, as before.");
            command.ExitCode = 1;
        }
        else if (argument.Length == 0)
        {
            command.Output = string.Format(UiText.Get("Terminal.ShellList", "Shells: {0}. Current: {1}."), names, ShellKinds.Name(sessions.Selected));
        }
        else if (!ShellKinds.TryParse(argument, out var kind) || !available.Contains(kind))
        {
            command.Error = string.Format(UiText.Get("Terminal.ShellUnknown", "shell: {0} is not available here. Available: {1}."), argument, names);
            command.ExitCode = 1;
        }
        else if (sessions.IsBusy || !sessions.Switch(kind))
        {
            command.Error = UiText.Get("Terminal.ShellBusy", "A command is running: the shell can be changed when it finishes.");
            command.ExitCode = 1;
        }
        else
        {
            command.Output = string.Format(UiText.Get("Terminal.ShellSwitched", "Shell is now {0}. The previous session's variables and folder are gone."), ShellKinds.Name(kind));
        }

        command.ExitTime = DateTime.Now;
        return Task.CompletedTask;
    }

    /// <summary>
    /// After a session command, moves the focused panel to the folder the session ended in. Only while the panel
    /// still shows the folder the command started from: a panel the user has moved on is left where it is (the
    /// stale-result rule, as for listings), and the next command then changes the session to the panel's folder.
    /// </summary>
    internal void FollowPanel(IContentModel model, Url startUrl, string folder)
    {
        if (!ReferenceEquals(contentModelService.GetLastContentModel(), model)
            || model.Url is null
            || startUrl is null
            || (string)model.Url != (string)startUrl)
        {
            return;
        }

        _ = contentModelService.SetUrlAsync(model.Name, new Url(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar));
    }
    #endregion

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal void AddCommandHistory(ITerminalCommand command)
    {
        History.Add(command);
        _recallIndex = -1;
        OnPropertyChanged(nameof(History));
        Command = new TerminalCommand();
    }
    #endregion
}
