using iConsole.Localization;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Adds a program to the "Open with" or "Edit with" list, or changes one: its name, the command,
/// the files it applies to and whether it runs in the terminal. Opened over
/// <see cref="OpenWithDialog"/>, which fills it in for the file under the cursor.
/// </summary>
public class FileAssociationDialog : FormDialog
{
    private readonly IFileAssociationService _associations;
    private readonly TextBox _textBoxName;
    private readonly TextBox _textBoxCommand;
    private readonly TextBox _textBoxPatterns;
    private readonly CheckBox _checkBoxTerminal;
    private readonly Label _hintLabel;
    private readonly Label _errorLabel;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    public FileAssociationDialog(
        IFileAssociationService associations,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FileAssociationDialog), themeService)
    {
        _associations = associations;
        Title = iConsole.Localization.UiText.Get("Dialog.Program", "Program");
        var saveCommand = new SaveCommand();
        AddCommand(saveCommand);

        // Captions padded to one width, so the three boxes start in the same column.
        _textBoxCommand = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Label.Command", "Command").PadRight(10), Placeholder = UiText.Get("Placeholder.CommandExample", "e.g. code %f") };
        _textBoxName = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Label.Name", "Name").PadRight(10) };
        _textBoxPatterns = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.AppliesTo", "Applies to"), Placeholder = "e.g. *.txt;*.md" };
        _checkBoxTerminal = new CheckBox(themeService) { Label = UiText.Get("Field.RunsInTerminal", "Runs in the terminal (vim, nano, less…)") };
        _hintLabel = new Label("%f file  %d folder  %n name — without %f the file goes last", themeService);
        _errorLabel = new Label(string.Empty, themeService);
        _saveButton = new Button(iConsole.Localization.UiText.Get("Common.Save", "Save"), saveCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        // The command first: it is the one field that has to be typed, and the first focusable
        // control is where the cursor lands.
        AddControl(_textBoxCommand);
        AddControl(_textBoxName);
        AddControl(_textBoxPatterns);
        AddControl(_checkBoxTerminal);
        AddControl(_hintLabel);
        AddControl(_errorLabel);
        AddControl(_saveButton);
        AddControl(_cancelButton);
    }

    /// <summary>Whether the program is for Enter or for F4; set by whoever opens the dialog.</summary>
    public FileAssociationKind Kind { get; set; }

    /// <summary>The association being changed, or null to add one.</summary>
    public FileAssociation? Existing { get; private set; }

    public string AssociationName
    {
        get => _textBoxName.Text ?? string.Empty;
        set => _textBoxName.Text = value ?? string.Empty;
    }

    public string Command
    {
        get => _textBoxCommand.Text ?? string.Empty;
        set => _textBoxCommand.Text = value ?? string.Empty;
    }

    public string Patterns
    {
        get => _textBoxPatterns.Text ?? string.Empty;
        set => _textBoxPatterns.Text = value ?? string.Empty;
    }

    public bool InTerminal
    {
        get => _checkBoxTerminal.IsChecked;
        set => _checkBoxTerminal.IsChecked = value;
    }

    public string Error => _errorLabel.Text;

    /// <summary>Fills the form for a new program that applies to <paramref name="patterns"/>.</summary>
    public void LoadNew(FileAssociationKind kind, string patterns)
    {
        Kind = kind;
        Existing = null;
        AssociationName = string.Empty;
        Command = string.Empty;
        Patterns = patterns;
        InTerminal = false;
    }

    /// <summary>Fills the form with <paramref name="association"/>, to be changed.</summary>
    public void LoadExisting(FileAssociation association)
    {
        Kind = association.Kind;
        Existing = association;
        AssociationName = association.Name;
        Command = association.Command;
        Patterns = association.Patterns;
        InTerminal = association.InTerminal;
    }

    public override async Task OnInitializeAsync()
    {
        var verb = Kind == FileAssociationKind.Edit ? "edit" : "open";
        Title = Existing is null ? $"Add a program to {verb} with" : $"Change the program to {verb} with";
        _errorLabel.Text = string.Empty;
        foreach (var textBox in new[] { _textBoxCommand, _textBoxName, _textBoxPatterns })
        {
            var length = (textBox.Text ?? string.Empty).Length;
            textBox.ScrollableModel.Reset();
            textBox.ScrollableModel.ScrollableSize = new Size(length, 1);
            textBox.ScrollableModel.CursorPosition.X = length;
        }

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(72, 11));
        }

        var row = 2;
        foreach (var control in new IControl[] { _textBoxCommand, _textBoxName, _textBoxPatterns, _checkBoxTerminal, _hintLabel, _errorLabel })
        {
            control.Position = new Position(FieldX, row++);
            control.ViewSize = new Size(FieldWidth, 1);
        }

        foreach (var textBox in new[] { _textBoxCommand, _textBoxName, _textBoxPatterns })
        {
            textBox.ScrollableModel.ViewSize = textBox.ViewSize;
        }

        LayoutButtonRow(_saveButton, _cancelButton);
    }

    protected override void ClearError() => _errorLabel.Text = string.Empty;

    /// <summary>
    /// Saves the form. False, with <see cref="Error"/> saying why, when there is no command or
    /// nothing it applies to. A name left empty is taken from the program.
    /// </summary>
    public bool Save()
    {
        var command = Command.Trim();
        var words = FileAssociation.SplitWords(command);
        if (words.Count == 0)
        {
            _errorLabel.Text = UiText.Get("Msg.TypeCommand", "Type the command that opens the file, such as notepad or code %f.");
            return false;
        }

        var patterns = Patterns.Trim();
        if (patterns.Length == 0)
        {
            _errorLabel.Text = UiText.Get("Msg.SayWhichFiles", "Say which files it applies to, such as *.jpg;*.png — or * for all.");
            return false;
        }

        var name = AssociationName.Trim();
        if (name.Length == 0)
        {
            name = Path.GetFileNameWithoutExtension(words[0]);
        }

        _associations.Save(new FileAssociation(
            Existing?.Id ?? Guid.NewGuid().ToString("N"),
            name,
            patterns,
            command,
            Kind,
            InTerminal));
        return true;
    }

    /// <summary>Enter, or the Save button: saves and closes; an incomplete form stays, saying what is missing.</summary>
    private sealed class SaveCommand : Command
    {
        public override string Title => "Save";
        public override string Description => "Save the program and return to the list.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as FileAssociationDialog ?? Control?.Parent as FileAssociationDialog;
            if (dialog is not null && dialog.Save())
            {
                dialog.Close();
            }

            return Task.CompletedTask;
        }
    }
}
