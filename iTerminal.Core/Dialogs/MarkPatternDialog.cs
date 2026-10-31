using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Num+ / Num-: a name pattern, and whether folders count. Marks (or unmarks) the matching rows of
/// the panel it was opened over and closes. The pattern is the one F7 takes — <c>*.jpg;*.png</c>,
/// or a bare word for "names containing it" — and the last one typed is offered again.
/// </summary>
public class MarkPatternDialog : FormDialog
{
    private readonly TextBox _textBoxPattern;
    private readonly CheckBox _checkBoxFolders;
    private readonly Button _applyButton;
    private readonly Button _cancelButton;
    private readonly Label _errorLabel;
    private readonly ApplyCommand _applyCommand;

    /// <summary>The panel to mark in; set by the command that opens the dialog.</summary>
    public IContentModel? Target { get; set; }

    /// <summary>True to mark the matching rows, false to unmark them.</summary>
    public bool Marking { get; set; } = true;

    public string Pattern
    {
        get => _textBoxPattern.Text ?? string.Empty;
        set => _textBoxPattern.Text = value ?? string.Empty;
    }

    public bool IncludeFolders
    {
        get => _checkBoxFolders.IsChecked;
        set => _checkBoxFolders.IsChecked = value;
    }

    public Label ErrorMessage => _errorLabel;

    protected override void ClearError() => _errorLabel.Text = string.Empty;

    public MarkPatternDialog(
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(MarkPatternDialog), themeService)
    {
        Title = iConsole.Localization.UiText.Get("Menu.Mark", "Mark");
        _applyCommand = new ApplyCommand(() => Marking);
        AddCommand(_applyCommand);

        _textBoxPattern = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Field.Pattern", "Pattern"),
            Text = "*",
            IsEnabled = true,
            HotKeyChar = 'P'
        };
        _checkBoxFolders = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.FoldersToo", "Folders too"),
            IsChecked = false,
            IsEnabled = true,
            HotKeyChar = 'F'
        };
        // Built from the command alone, so its caption follows the command's title: Mark or Unmark.
        _applyButton = new Button(_applyCommand, themeService) { HotKeyChar = 'M' };
        _cancelButton = new CancelDialogButton(themeService);
        _errorLabel = new Label(string.Empty, themeService);
        AddControl(_textBoxPattern);
        AddControl(_checkBoxFolders);
        AddControl(_errorLabel);
        AddControl(_applyButton);
        AddControl(_cancelButton);
    }

    public override async Task OnInitializeAsync()
    {
        Title = Marking
            ? UiText.Get("Dialog.MarkFiles", "Mark files")
            : UiText.Get("Dialog.UnmarkFiles", "Unmark files");
        _applyButton.HotKeyChar = Marking ? 'M' : 'U';
        _errorLabel.Text = string.Empty;
        _textBoxPattern.ScrollableModel.Reset();
        _textBoxPattern.ScrollableModel.ScrollableSize = new Size(Pattern.Length, 1);
        _textBoxPattern.ScrollableModel.CursorPosition.X = Pattern.Length;

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(60, 7));
        }

        _textBoxPattern.Position = new Position(FieldX, 2);
        _textBoxPattern.ViewSize = new Size(FieldWidth, 1);
        _textBoxPattern.ScrollableModel.ViewSize = _textBoxPattern.ViewSize;
        _checkBoxFolders.ViewSize = new Size(FieldWidth, 1);
        _errorLabel.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_applyButton, _cancelButton);

        Layout.Bottom(_checkBoxFolders, _textBoxPattern);
        Layout.Bottom(_errorLabel, _checkBoxFolders);
    }

    /// <summary>Applies the pattern to <see cref="Target"/>; returns how many rows changed.</summary>
    internal int Apply()
    {
        if (Target is not IMarkableContentModel { CanMark: true } marks)
        {
            return 0;
        }

        var changed = marks.SetMarks(Pattern, Marking, IncludeFolders);
        Target.Update();
        return changed;
    }

    /// <summary>Enter, or the Mark/Unmark button: applies the pattern and closes.</summary>
    private sealed class ApplyCommand(Func<bool> marking) : Command
    {
        public override string Title => marking() ? "Mark" : "Unmark";
        public override string Description => "Mark or unmark the rows matching the pattern.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as MarkPatternDialog ?? Control?.Parent as MarkPatternDialog;
            if (dialog is null)
            {
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(dialog.Pattern))
            {
                dialog.ErrorMessage.Text = UiText.Get("Msg.TypePattern", "Type a pattern, such as *.jpg;*.png.");
                return Task.CompletedTask;
            }

            // Nothing to mark in, or nothing matching: say so and keep the dialog open to fix the pattern, rather
            // than closing as though the mark had worked.
            if (dialog.Target is not IMarkableContentModel { CanMark: true })
            {
                dialog.ErrorMessage.Text = UiText.Get("Msg.NoPanelMark", "There is no panel to mark in.");
                return Task.CompletedTask;
            }

            if (dialog.Apply() == 0)
            {
                dialog.ErrorMessage.Text = dialog.Marking
                    ? $"No file matches {dialog.Pattern.Trim()}."
                    : $"No marked file matches {dialog.Pattern.Trim()}.";
                return Task.CompletedTask;
            }

            dialog.Close();
            return Task.CompletedTask;
        }
    }
}
