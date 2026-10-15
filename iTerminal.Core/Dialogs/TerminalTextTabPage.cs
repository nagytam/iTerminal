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
    internal const string NoCommandText = "No terminal command has been run yet.";

    private ITerminalModel _terminalModel { get; set; }
    private TextViewerControl _textViewerControl { get; set; }

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
        var command = _terminalModel.History.LastOrDefault();
        List<string> lines;
        if (command is null)
        {
            lines = [NoCommandText];
        }
        else
        {
            var text = SelectText(command);
            lines = string.IsNullOrWhiteSpace(text)
                ? [EmptyText]
                // Either line ending: a tool's output is not bound to the OS's.
                : [.. text.TrimEnd().Split('\n').Select(line => line.TrimEnd('\r'))];
        }

        _textViewerControl.TextModel.Lines.FromList(lines);
        await base.OnInitializeAsync();
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
