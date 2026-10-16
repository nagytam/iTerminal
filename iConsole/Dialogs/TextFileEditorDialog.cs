using iConsole.Controls.Editors;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;

namespace iConsole.Dialogs;

public class TextFileEditorDialog : Dialog
{
    public override string Title
    {
        get => base.Title + (TextEditorControl.IsDirty ? " (*)" : string.Empty);
        set => base.Title = value;
    }
    public Url? Url { get; set; } = null;
    private IContentModelService _contentModelService { get; set; }
    private IFileToTextConverterService _fileToTextConverterService { get; set; }
    public TextEditorControl TextEditorControl { get; set; }
    private EditorStatusBar _statusBar { get; set; }

    public TextFileEditorDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(TextFileEditorDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;

        TextEditorControl = new TextEditorControl(themeService);
        _statusBar = new EditorStatusBar(
            TextEditorControl,
            TextEditorControl,
            TextEditorControl,
            TextEditorControl.ScrollableModel,
            TextEditorControl.SearchModel,
            TextEditorControl.TextModel,
            contentModelService,
            beepService,
            dateTimeProviderService,
            themeService);
        AddControl(TextEditorControl);
        AddControl(_statusBar);
    }

    public override async Task OnInitializeAsync()
    {
        var fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, TextEditorControl.TextModel);

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = Url?.Shrink((int)ViewSize.Width - 4) ?? "Text file editor";
        }

        TextEditorControl.Position = new(1, 1);
        // Ensure we never set negative dimensions when the dialog has no parent
        var editorWidth = Math.Max(0L, ViewSize.Width - 2);
        var editorHeight = Math.Max(0L, ViewSize.Height - 3);
        TextEditorControl.ViewSize = new(editorWidth, editorHeight);

        var statusY = Math.Max(0L, ViewSize.Height - 2);
        var statusWidth = Math.Max(0L, ViewSize.Width - 2);
        _statusBar.Position = new Position(1, statusY);
        _statusBar.ViewSize = new(statusWidth, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
