using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Labels;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Filter by Label: shows in the panel only the files with the colour and/or the tag picked here. Folders and the parent row
/// stay listed so the panel can still be walked. Any colour and a blank tag clear the filter. Enter applies; Esc closes.
/// </summary>
public class LabelFilterDialog : FormDialog
{
    private const string AnyColour = "any";
    private const string NoColour = "none";

    private readonly IFileLabelService _labels;
    private readonly ComboBox<string> _colour;
    private readonly TextBox _tag;
    private readonly Label _info;
    private readonly Label _message;
    private readonly Button _applyButton;
    private readonly Button _cancelButton;
    private ContentModelBase? _model;

    public LabelFilterDialog(
        IFileLabelService labels,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(LabelFilterDialog), themeService)
    {
        _labels = labels;
        Title = "Filter by label";
        var applyCommand = new ApplyCommand();
        AddCommand(applyCommand);

        _colour = new ComboBox<string>(themeService) { Label = "Colour" };
        _colour.SetItems([AnyColour, NoColour, .. Enum.GetValues<LabelColour>().Where(colour => colour != LabelColour.None).Select(LabelColours.Name)]);
        _tag = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Tag" };
        _info = new Label("Folders stay listed, so the panel can still be walked.", themeService);
        _message = new Label(string.Empty, themeService);
        _applyButton = new Button("Apply", applyCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_colour);
        AddControl(_tag);
        AddControl(_info);
        AddControl(_message);
        AddControl(_applyButton);
        AddControl(_cancelButton);
    }

    /// <summary>The colour picked: a colour name, "none" for files without one, or "any".</summary>
    public string? ColourChoice => _colour.SelectedItem;

    public string Tag
    {
        get => _tag.Text ?? string.Empty;
        set => _tag.Text = value ?? string.Empty;
    }

    /// <summary>What is wrong with the fields, or empty.</summary>
    public string Message => _message.Text;

    /// <summary>Sets the panel to filter; call before showing the dialog.</summary>
    public void Load(ContentModelBase model)
    {
        _model = model;
        Tag = string.Empty;
        _colour.SelectedIndex = 0;
        _message.Text = string.Empty;
    }

    /// <summary>Picks the colour by name, as the list shows it: a colour, "none" or "any".</summary>
    public void ChooseColour(string name)
    {
        var index = _colour.Items.ToList().IndexOf(name);
        if (index >= 0)
        {
            _colour.SelectedIndex = index;
        }
    }

    /// <summary>The filter the fields describe.</summary>
    public LabelFilter Filter()
    {
        var choice = ColourChoice;
        LabelColour? colour = choice is null or AnyColour ? null : LabelColours.Parse(choice);
        var tag = Tag.Trim();
        return new LabelFilter(colour, tag.Length == 0 ? null : tag);
    }

    /// <summary>Applies the filter to the panel and closes. False, with <see cref="Message"/> saying why, when the tag is not one tag.</summary>
    public bool Apply()
    {
        if (_model is null)
        {
            Close();
            return true;
        }

        var filter = Filter();
        if (filter.Tag is { } tag)
        {
            if (tag.Contains(','))
            {
                _message.Text = "One tag at a time.";
                StateHasChanged();
                return false;
            }

            if (FileLabelTags.Validate(tag) is { } problem)
            {
                _message.Text = problem;
                StateHasChanged();
                return false;
            }
        }

        _model.SetEntryFilter(filter.IsEmpty ? null : filter.ToEntryFilter(_labels), filter.IsEmpty ? null : filter.Describe());
        Close();
        return true;
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 70), 10));
        }

        _colour.Position = new Position(FieldX, 1);
        _colour.ViewSize = new Size(FieldWidth, 1);
        _tag.Position = new Position(FieldX, 3);
        _tag.ViewSize = new Size(FieldWidth, 1);
        _tag.ScrollableModel.ScrollableSize = new Size(_tag.MaxLength, 1);
        _tag.ScrollableModel.ViewSize = new Size(Math.Max(0, _tag.EditableWidth), 1);
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
        public override string Description => "Filter the panel and close.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as LabelFilterDialog ?? Control?.Parent as LabelFilterDialog;
            dialog?.Apply();
            return Task.CompletedTask;
        }
    }
}
