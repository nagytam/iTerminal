using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Labels;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Gives the marked files and folders (or the one under the cursor) a colour and tags. For one item the fields show what it
/// has, and a blank tag list removes them. For several, the colour and the tags start at "(keep)" and blank, and stay as
/// each item has them unless changed. Enter saves; Esc closes without saving.
/// </summary>
public class FileLabelsDialog : FormDialog
{
    private const string Keep = "(keep)";

    private readonly IFileLabelService _labels;
    private readonly IContentModelService? _contentModelService;
    private readonly ComboBox<string> _colour;
    private readonly TextBox _tags;
    private readonly Label _info;
    private readonly Label _message;
    private readonly Button _applyButton;
    private readonly Button _cancelButton;
    private IReadOnlyList<string> _paths = [];
    private Task? _saving;
    private bool _saveSucceeded;
    private string? _failure;

    public FileLabelsDialog(
        IFileLabelService labels,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IContentModelService? contentModelService = null)
        : base(nameof(FileLabelsDialog), themeService)
    {
        _labels = labels;
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.ColourLabels", "Colour labels and tags");
        var applyCommand = new ApplyCommand();
        AddCommand(applyCommand);

        _colour = new ComboBox<string>(themeService) { Label = iConsole.Localization.UiText.Get("Label.Colour", "Colour") };
        _tags = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Tags" };
        _info = new Label(string.Empty, themeService);
        _message = new Label(string.Empty, themeService);
        _applyButton = new Button(iConsole.Localization.UiText.Get("Common.Apply", "Apply"), applyCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_colour);
        AddControl(_tags);
        AddControl(_info);
        AddControl(_message);
        AddControl(_applyButton);
        AddControl(_cancelButton);
    }

    /// <summary>The colour the dialog shows: a colour name, or "(keep)" when the colour stays as each item has it.</summary>
    public string? ColourChoice => _colour.SelectedItem;

    public string Tags
    {
        get => _tags.Text ?? string.Empty;
        set => _tags.Text = value ?? string.Empty;
    }

    /// <summary>What is wrong with the fields, or the save's error; empty when there is nothing to say.</summary>
    public string Message => _message.Text;

    /// <summary>True while the labels are being written.</summary>
    public bool IsSaving => _saving is not null && !_saving.IsCompleted;

    /// <summary>The save in progress, so a test can await it.</summary>
    public Task? SaveTask => _saving;

    /// <summary>Sets what to label; call before showing the dialog.</summary>
    public void Load(IReadOnlyList<string> paths)
    {
        _paths = paths;
    }

    /// <summary>Picks the colour by name, as the list shows it.</summary>
    public void ChooseColour(string name)
    {
        var index = _colour.Items.ToList().IndexOf(name);
        if (index >= 0)
        {
            _colour.SelectedIndex = index;
        }
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        await _labels.WhenLoadedAsync();
        var single = _paths.Count == 1;
        var names = Enum.GetValues<LabelColour>().Select(LabelColours.Name).ToList();
        _colour.SetItems(single ? names : [Keep, .. names]);
        if (single)
        {
            var label = _labels.Get(_paths[0]);
            ChooseColour(LabelColours.Name(label.Colour));
            Tags = FileLabelTags.Format(label.Tags);
            _info.Text = "Blank tags remove them. Tags are separated by commas.";
        }
        else
        {
            ChooseColour(Keep);
            Tags = string.Empty;
            _info.Text = $"{_paths.Count} items. Leave (keep) and the tags blank to change only what you type.";
        }

        Title = single ? "Colour labels and tags — " + Path.GetFileName(_paths[0]) : "Colour labels and tags";
        StateHasChanged();
    }

    /// <summary>Saves the fields to every item in the background. False, and the reason in <see cref="Message"/>, when the tags are not valid.</summary>
    public bool StartSave()
    {
        if (IsSaving)
        {
            return false;
        }

        if (FileLabelTags.Validate(Tags) is { } problem)
        {
            _message.Text = problem;
            StateHasChanged();
            return false;
        }

        var choice = ColourChoice;
        LabelColour? colour = choice is null || choice == Keep ? null : LabelColours.Parse(choice);
        var keepTags = _paths.Count > 1 && string.IsNullOrWhiteSpace(Tags);
        var tags = FileLabelTags.Parse(Tags);
        _message.Text = "Saving…";
        _saving = SaveAsync(_paths, old => new FileLabel(colour ?? old.Colour, keepTags ? old.Tags : tags));
        StateHasChanged();
        return true;
    }

    private async Task SaveAsync(IReadOnlyList<string> paths, Func<FileLabel, FileLabel> change)
    {
        try
        {
            await _labels.UpdateAsync(paths, change);
            _saveSucceeded = true;
        }
        catch (Exception exception)
        {
            _failure = exception.Message;
        }
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_saving is { IsCompleted: true } && _message.Text == "Saving…")
        {
            if (_saveSucceeded)
            {
                // The panel draws the new labels from memory, and closing the dialog repaints it. A panel filtered by label
                // has its list built from them, so that one is rebuilt.
                PanelRefreshHelper.RefreshEntryFilters(_contentModelService);
                _message.Text = string.Empty;
                Close();
            }
            else
            {
                _message.Text = _failure is null ? "Could not save the labels." : $"Could not save the labels: {_failure}";
            }

            StateHasChanged();
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 72), 11));
        }

        _colour.Position = new Position(FieldX, 1);
        _colour.ViewSize = new Size(FieldWidth, 1);
        _tags.Position = new Position(FieldX, 3);
        _tags.ViewSize = new Size(FieldWidth, 1);
        _tags.ScrollableModel.ScrollableSize = new Size(_tags.MaxLength, 1);
        _tags.ScrollableModel.ViewSize = new Size(Math.Max(0, _tags.EditableWidth), 1);
        _info.Position = new Position(FieldX, 5);
        _info.ViewSize = new Size(FieldWidth, 1);
        _message.Position = new Position(FieldX, 6);
        _message.ViewSize = new Size(FieldWidth, 1);
        base.OnInitializeLayout();
        LayoutButtonRow(_applyButton, _cancelButton);
    }

    /// <summary>Enter, or the Apply button.</summary>
    private sealed class ApplyCommand : Command
    {
        public override string Title => "Apply";
        public override string Description => "Save the colour and the tags and close.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as FileLabelsDialog ?? Control?.Parent as FileLabelsDialog;
            dialog?.StartSave();
            return Task.CompletedTask;
        }
    }
}
