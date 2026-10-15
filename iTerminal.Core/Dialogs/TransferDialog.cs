using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Base for the Copy and Move dialogs: a source label, a target path textbox with validation, an
/// overwrite checkbox, and a primary/cancel button row. The two dialogs differ only in the
/// operation name shown to the user and the <see cref="Command"/> the primary button runs.
/// </summary>
public abstract class TransferDialog : FormDialog
{
    readonly IContentModelService _contentModelService;
    private readonly string _operationName;

    public Url SourceUrl { get; set; }

    /// <summary>
    /// Everything the operation acts on: the marked rows of the source panel, or just
    /// <see cref="SourceUrl"/>. With more than one, <see cref="TargetUrl"/> is the folder they all
    /// go into, each under its own name (<see cref="TargetUrlFor"/>).
    /// </summary>
    public IReadOnlyList<Url> SourceUrls { get; set; } = [];

    /// <summary>True when several entries are copied or moved at once.</summary>
    public bool IsMultiple => SourceUrls.Count > 1;

    /// <summary>The panel the sources were taken from, set by the command that opens the dialog.</summary>
    public IContentModel? SourceModel { get; set; }
    public Url TargetUrl
    {
        get
        {
            var targetName = TextBoxTarget?.Text ?? string.Empty;
            if (Path.GetFileName(targetName) == targetName)
            {
                if (BaseUrl is not null)
                {
                    targetName = BaseUrl.Combine(targetName);
                }
            }
            return targetName;
        }
        set
        {
            if (TextBoxTarget.Text != value)
            {
                TextBoxTarget.Text = value;
            }
        }
    }

    public TextBox TextBoxTarget { get; private set; }
    private CheckBox _checkBoxOverwrite { get; set; }
    public bool Overwrite => _checkBoxOverwrite.IsChecked;
    private Label _labelSource { get; set; }
    private Label _labelTarget { get; set; }
    public ErrorMessage ErrorMessage { get; private set; }
    private Button _primaryButton { get; set; }
    private Button _cancelButton { get; set; }
    public Url BaseUrl { get; internal set; }

    protected TransferDialog(
        string name,
        string operationName,
        Command transferCommand,
        char primaryHotKeyChar,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(name, themeService)
    {
        _contentModelService = contentModelService;
        _operationName = operationName;
        Title = operationName;

        AddCommand(transferCommand);

        _labelSource = new Label("Source: ", themeService);
        TextBoxTarget = new FolderOrNewFileTextBox(beepService, dateTimeProviderService, themeService)
        {
            // The box holds a destination path, not a name; "Name" was a leftover from the create
            // dialogs and read as "rename to".
            Label = "To",
            Text = TargetUrl,
            MaxLength = Url.MaxLength,
            IsReadOnly = false
        };
        TextBoxTarget.PropertyChanged += (_, _) => UpdateTargetPreview();
        var validator = (TextBoxTarget.ValidationModel as FolderOrNewFilesValidationModel);
        ErrorMessage = new ErrorMessage(validator, themeService);

        _labelTarget = new Label("Target: ", themeService);
        _checkBoxOverwrite = new CheckBox(themeService)
        {
            Label = "Overwrite",
            IsChecked = false,
            IsEnabled = true,
            HotKeyChar = 'O'
        };
        _primaryButton = new Button(operationName, transferCommand, themeService) { HotKeyChar = primaryHotKeyChar };
        _cancelButton = new CancelDialogButton(themeService);
        AddControl(_labelSource);
        AddControl(TextBoxTarget);
        AddControl(_labelTarget);
        AddControl(_checkBoxOverwrite);
        AddControl(ErrorMessage);
        AddControl(_primaryButton);
        AddControl(_cancelButton);
    }

    public override async Task OnInitializeAsync()
    {
        BaseUrl = _contentModelService.Url;
        var sourceModel = SourceModel ?? _contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel();
        var sources = iTerminal.Core.Commands.FocusedEntry.ActionableUrls(sourceModel);
        var url = sources.Count > 0 ? sources[0] : _contentModelService.FocusedFileUrl;
        var targetFolder = _contentModelService.NonFocusedFolderUrl;
        ArgumentNullException.ThrowIfNull(url);
        SourceUrl = url;
        SourceUrls = sources.Count > 0 ? sources : [url];
        TargetUrl = IsMultiple ? new Url(targetFolder) : new Url(targetFolder).Combine(url.Name);
        TextBoxTarget.Text = TargetUrl.FullUrl;
        TextBoxTarget.ScrollableModel.Reset();
        TextBoxTarget.ScrollableModel.ScrollableSize = new Size(TextBoxTarget.Text.Length, 1);
        TextBoxTarget.ScrollableModel.ViewSize = TextBoxTarget.ViewSize;

        if (IsMultiple)
        {
            Title = $"{_operationName} {SourceUrls.Count} items";
        }
        else if (SourceUrl.PathType == PathType.File)
        {
            Title = $"{_operationName} file";
        }
        else
        {
            Title = $"{_operationName} folder";
        }

        await base.OnInitializeAsync();
    }

    /// <summary>Rows: border, blank, source, target box, resolved target, overwrite, error, blank, buttons, border.</summary>
    internal const int DialogHeight = 10;

    public override void OnInitializeLayout()
    {
        // Sized first: the rows below are laid out from FieldWidth, which is read from ViewSize, and
        // the base pass lays the children out from what is assigned here. It used to run the other
        // way round, so the first layout of a freshly opened dialog used the previous size.
        if (Parent != null)
        {
            CenterInParent(new Size(80, DialogHeight));
        }

        _labelSource.Text = "Source: " + (IsMultiple
            ? DescribeSources(SourceUrls).Shrink((int)FieldWidth - 8)
            : SourceUrl.Shrink((int)FieldWidth - 8));
        // One column for every row — the box's caption, the two path lines, the check box and the
        // error — as in the rename and delete dialogs; they used to start two columns apart.
        _labelSource.Position = new Position(FieldX, 2);
        _labelSource.ViewSize = new Size(FieldWidth, 1);
        TextBoxTarget.Position = new Position(FieldX, 3);
        TextBoxTarget.ViewSize = new Size(FieldWidth, 1);
        _labelTarget.Position = new Position(FieldX, 4);
        _labelTarget.ViewSize = new Size(FieldWidth, 1);
        _checkBoxOverwrite.Position = new Position(FieldX, 5);
        _checkBoxOverwrite.ViewSize = new Size(FieldWidth, 1);
        ErrorMessage.Position = new Position(FieldX, 6);
        ErrorMessage.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_primaryButton, _cancelButton);

        base.OnInitializeLayout();

        UpdateTargetPreview();
    }

    /// <summary>
    /// The resolved destination under the box. A bare name typed into the box is resolved against
    /// the source's folder, which is not what the box shows — so the line spells out where the copy
    /// will actually land. It used to be computed once when the dialog opened, and then showed the
    /// original target however the box was edited.
    /// </summary>
    private void UpdateTargetPreview()
    {
        if (_labelTarget is null || TextBoxTarget is null)
        {
            return;
        }

        string target;
        try
        {
            target = TargetUrl?.FullUrl ?? string.Empty;
        }
        catch
        {
            target = TextBoxTarget.Text ?? string.Empty;
        }

        string text = "Target: " + target.Shrink((int)FieldWidth - 8);
        if (_labelTarget.Text != text)
        {
            _labelTarget.Text = text;
            StateHasChanged();
        }
    }

    internal string TargetPreviewForTest => _labelTarget.Text;

    internal string SourceLabelForTest => _labelSource.Text;

    /// <summary>"3 files, 2 folders: a.txt, b.txt, …" — what the source line says for a multiple selection.</summary>
    internal static string DescribeSources(IReadOnlyList<Url> sources)
    {
        var folders = sources.Count(u => u.PathType == PathType.Folder);
        var files = sources.Count - folders;
        var parts = new List<string>();
        if (files > 0)
        {
            parts.Add($"{files} file{(files == 1 ? string.Empty : "s")}");
        }
        if (folders > 0)
        {
            parts.Add($"{folders} folder{(folders == 1 ? string.Empty : "s")}");
        }

        return string.Join(", ", parts) + ": " + string.Join(", ", sources.Select(u => iTerminal.Core.Commands.FocusedEntry.NameOf(u)));
    }

    /// <summary>
    /// Where <paramref name="source"/> lands when several entries go into the folder in the box: that
    /// folder, under the source's own name. Built from the text rather than with
    /// <see cref="Url.Combine"/>, which only combines onto a URL already known to be a folder — and
    /// the folder typed may not exist yet (the operation creates it).
    /// </summary>
    public Url TargetUrlFor(Url source)
    {
        var name = iTerminal.Core.Commands.FocusedEntry.NameOf(source);
        return new Url(Helper.UrlPath.Child(TargetUrl.FullUrl, name, source.PathType == PathType.Folder));
    }

    public void Reset()
    {
        BaseUrl = string.Empty;
        SourceUrl = string.Empty;
        SourceUrls = [];
        SourceModel = null;
        TargetUrl = string.Empty;
        ErrorMessage.Text = string.Empty;
    }
}
