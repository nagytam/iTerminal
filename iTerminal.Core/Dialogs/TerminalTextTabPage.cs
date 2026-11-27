using iConsole.Controls.Tabs;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Tab page showing one text of the last executed terminal command in a read-only viewer.
/// Subclasses pick the text via <see cref="SelectText"/>.
///
/// <para>A page with nothing to show says so. It used to shrink its viewer to nothing, so before the
/// first command — and on the error page of every command that succeeded — the dialog was an empty
/// frame that read as broken rather than as "there is nothing here".</para>
/// </summary>
public abstract class TerminalTextTabPage : TabPage
{
    internal static string NoCommandText => iConsole.Localization.UiText.Get("Terminal.NoCommand", "No terminal command has been run yet.");

    internal static string RunningText => iConsole.Localization.UiText.Get("Terminal.Running", "The command is running. Its output appears here when it finishes.");

    /// <summary>
    /// The command the terminal dialog shows: the one running now, otherwise the last one that finished. A command started from
    /// a panel is shown while it runs, so the dialog opened on it is not empty for as long as it takes.
    /// </summary>
    internal static ITerminalCommand? ShownCommand(ITerminalModel terminalModel) =>
        terminalModel.Command.CommandTask is not null ? terminalModel.Command : terminalModel.History.LastOrDefault();

    private ITerminalModel _terminalModel { get; set; }
    private TextViewerControl _textViewerControl { get; set; }
    private ITerminalCommand? _shown;
    private bool _shownRunning;

    protected TerminalTextTabPage(
        string title,
        ConsoleKey hotConsoleKey,
        ITerminalModel terminalModel,
        IThemeService themeService)
        : base(title, themeService)
    {
        _terminalModel = terminalModel;
        HotConsoleKey = hotConsoleKey;

        _textViewerControl = new TextViewerControl(false, themeService);
        AddControl(_textViewerControl);
    }

    /// <summary>Selects which text of the command this tab displays.</summary>
    protected abstract string? SelectText(ITerminalCommand command);

    /// <summary>What the page says when the command has none of its text.</summary>
    protected abstract string EmptyText { get; }

    internal IReadOnlyList<string> LinesForTest => [.. _textViewerControl.TextModel.Lines.AsSnapshot()];

    public override async Task OnInitializeAsync()
    {
        Load();
        await base.OnInitializeAsync();
    }

    /// <summary>
    /// The command starts and ends while the dialog is open: the page reloads when the shown command changes or
    /// finishes. It runs on the idle pass, on the thread that owns the controls, so the worker that finished the
    /// command never touches them.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        var command = ShownCommand(_terminalModel);
        if (!ReferenceEquals(command, _shown) || (command?.CommandTask is not null) != _shownRunning)
        {
            Load();
        }
    }

    private void Load()
    {
        var command = ShownCommand(_terminalModel);
        _shown = command;
        _shownRunning = command?.CommandTask is not null;

        List<string> lines;
        if (command is null)
        {
            lines = [NoCommandText];
        }
        else
        {
            var text = SelectText(command);
            if (string.IsNullOrWhiteSpace(text))
            {
                lines = [_shownRunning ? RunningText : EmptyText];
            }
            else
            {
                // Either line ending: a tool's output is not bound to the OS's.
                lines = [.. text.TrimEnd().Split('\n').Select(line => line.TrimEnd('\r'))];
            }
        }

        _textViewerControl.TextModel.Lines.FromList(lines);
    }

    public override void OnInitializeLayout()
    {
        _textViewerControl.Position = new Position(1, 1);
        _textViewerControl.ViewSize = new Size(
            Math.Max(0, (int)ViewSize.Width - 2),
            Math.Max(0, (int)ViewSize.Height - 3));
        base.OnInitializeLayout();
    }
}
