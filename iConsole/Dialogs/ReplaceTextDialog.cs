using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// Ctrl+R in the editor: what to find and what to put in its place. "Replace" does the next occurrence from the
/// cursor, "Replace all" every one, as a single undo step. The dialog remembers what was asked last in the editor.
/// </summary>
public class ReplaceTextDialog : Dialog
{
    private readonly TextEditorControl _editor;
    private readonly IBeepService _beepService;
    private readonly TextBox _findBox;
    private readonly TextBox _replaceBox;
    private readonly CheckBox _matchCaseBox;
    private readonly CheckBox _regexBox;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;
    private readonly Button _cancelButton;

    public ReplaceTextDialog(
        TextEditorControl editor,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base("Replace text", themeService)
    {
        _editor = editor;
        _beepService = beepService;
        _findBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "Find",
            HotKeyChar = 'F',
            Text = editor.LastFind,
        };
        _replaceBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "With",
            HotKeyChar = 'W',
            Text = editor.LastReplacement,
        };
        _matchCaseBox = new CheckBox(themeService) { Label = "Match case", IsChecked = editor.LastMatchCase };
        _regexBox = new CheckBox(themeService) { Label = "Regular expression", IsChecked = editor.LastUseRegex };
        _replaceButton = new Button("Replace", new ReplaceCommand(this, all: false), themeService) { HotKeyChar = 'R' };
        _replaceAllButton = new Button("Replace all", new ReplaceCommand(this, all: true), themeService) { HotKeyChar = 'A' };
        _cancelButton = new CancelDialogButton(themeService);
        AddControl(_findBox);
        AddControl(_replaceBox);
        AddControl(_matchCaseBox);
        AddControl(_regexBox);
        AddControl(_replaceButton);
        AddControl(_replaceAllButton);
        AddControl(_cancelButton);
    }

    public string FindValue => _findBox.Text;

    public string ReplacementValue => _replaceBox.Text;

    public bool MatchCase => _matchCaseBox.IsChecked;

    /// <summary>The find text is a regular expression, and the replacement may use <c>$1</c>.</summary>
    public bool UseRegex => _regexBox.IsChecked;

    /// <summary>How many occurrences the last "Replace" or "Replace all" changed.</summary>
    public int ReplacedCount { get; private set; }

    /// <summary>Does the replacing; beeps when there was nothing to replace, closes the dialog when there was.</summary>
    internal void Replace(bool all)
    {
        _editor.LastFind = FindValue;
        _editor.LastReplacement = ReplacementValue;
        _editor.LastMatchCase = MatchCase;
        _editor.LastUseRegex = UseRegex;
        ReplacedCount = all
            ? _editor.ReplaceAll(FindValue, ReplacementValue, MatchCase, UseRegex)
            : _editor.ReplaceNext(FindValue, ReplacementValue, MatchCase, UseRegex) ? 1 : 0;
        if (ReplacedCount == 0)
        {
            _beepService.Beep();
            return;
        }

        Close();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(64, 11));
        }

        var width = Math.Max(0, ViewSize.Width - 6);
        foreach (var box in new[] { _findBox, _replaceBox })
        {
            box.ViewSize = new Size(width, 1);
            box.ScrollableModel.ScrollableSize = new Size(box.MaxLength, 1);
            box.ScrollableModel.ViewSize = new Size(Math.Max(0, box.EditableWidth), 1);
        }

        _findBox.Position = new Position(3, 2);
        _replaceBox.Position = new Position(3, 4);
        _matchCaseBox.Position = new Position(3, 6);
        _matchCaseBox.ViewSize = new Size(16, 1);
        _regexBox.Position = new Position(24, 6);
        _regexBox.ViewSize = new Size(24, 1);

        var buttonY = Math.Max(0, ViewSize.Height - 2);
        _replaceButton.Position = new Position(3, buttonY);
        _replaceButton.ViewSize = new Size(15, 1);
        _replaceAllButton.Position = new Position(20, buttonY);
        _replaceAllButton.ViewSize = new Size(17, 1);
        _cancelButton.Position = new Position(Math.Max(0, ViewSize.Width - 17), buttonY);
        _cancelButton.ViewSize = new Size(15, 1);
    }

    /// <summary>Enter on the "Replace" button replaces the next one; the other button has no key of its own.</summary>
    private sealed class ReplaceCommand(ReplaceTextDialog dialog, bool all) : Command
    {
        public override string Title => all ? "Replace all" : "Replace";

        public override Key? Key { get; init; } = all ? null : new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? arg)
        {
            dialog.Replace(all);
            return Task.CompletedTask;
        }
    }
}
