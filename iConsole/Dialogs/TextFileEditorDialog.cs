using iConsole.Commands;
using iConsole.Controls.Editors;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Helper;
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
            themeService,
            TextEditorControl);
        // The same key as the viewer's: F5 asks for a line number and goes to it.
        AddCommand(new ViewerGoToLineCommand(TextEditorControl, beepService, dateTimeProviderService, themeService));
        // F6 wraps the lines in the editor, as F2 does in the viewer; F2 is save here. Wrapped, Up and Down walk the rows.
        AddCommand(new ViewerWrapLinesCommand(TextEditorControl, new iConsole.Data.Key(ConsoleKey.F6, false, false, false)));
        AddControl(TextEditorControl);
        AddControl(_statusBar);
    }

    /// <summary>Writes the text to its file the way F2 does, keeping the line break and code page it was read with.</summary>
    public Task<bool> SaveAsync() => SaveCommand.WriteAsync(this, TextEditorControl.TextModel, _contentModelService);

    private bool _asking;
    private bool _closeConfirmed;

    /// <summary>The question asked by the last Close of a changed file. Nothing waits on it; it exists so a test can.</summary>
    public Task PendingClose { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Closing a file that has unsaved changes asks first (Save, Discard or Cancel); Esc, the Close button and F10 all
    /// come here. A read-only editor has nothing to lose and closes at once.
    /// </summary>
    public override void Close()
    {
        if (_closeConfirmed || !TextEditorControl.IsDirty || TextEditorControl.IsReadOnly)
        {
            _closeConfirmed = false;
            base.Close();
            return;
        }

        if (!_asking)
        {
            PendingClose = AskBeforeClosingAsync();
        }
    }

    private async Task AskBeforeClosingAsync()
    {
        _asking = true;
        try
        {
            var prompt = new UnsavedChangesDialog(Url?.Name is { Length: > 0 } name ? name : "This file", ThemeService);
            await ShowDialogAsync(prompt);
            switch (await prompt.Choice)
            {
                case UnsavedChoice.Save:
                    try
                    {
                        if (await SaveAsync())
                        {
                            _closeConfirmed = true;
                            Close();
                        }
                        else
                        {
                            Title = "Nowhere to save to: " + (Url?.Shrink((int)ViewSize.Width - 30) ?? "no file");
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Stay in the editor with the text: closing now would lose what could not be written.
                        Title = "Not saved: " + exception.Message;
                    }

                    StateHasChanged();
                    break;
                case UnsavedChoice.Discard:
                    _closeConfirmed = true;
                    Close();
                    break;
            }
        }
        finally
        {
            _asking = false;
        }
    }

    public override async Task OnInitializeAsync()
    {
        var fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, TextEditorControl.TextModel);
        TextEditorControl.Syntax = SyntaxHighlighter.ForFile(Url is null ? null : (Url.Path.Length > 0 ? Url.Path : Url.FullUrl));

        await base.OnInitializeAsync();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Ctrl+S", "Save the file");
        yield return ("Tab", "Indent");
        yield return ("F8", "Next code page (Shift: previous)");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F8
            && !keyEvent.Control && !keyEvent.Alt)
        {
            SwitchEncoding(backwards: keyEvent.Shift);
            return true;
        }

        // Tab indents in the editor: it is not for moving between the status bar's buttons, which have keys of their own.
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Tab
            && !keyEvent.Control && !keyEvent.Alt
            && TextEditorControl.IsEnabled)
        {
            return await TextEditorControl.HandleKeyEventAsync(keyEvent);
        }

        // Ctrl+S as well as F2: the key everybody types to save.
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.S
            && keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            if (TextEditorControl.IsDirty && !TextEditorControl.IsReadOnly)
            {
                await SaveAsync();
            }

            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// F8 / Shift+F8: reads the text under the next / previous code page, and saves with it. Unsaved
    /// changes are kept — the text is re-read in memory, not from the disk — and the file counts as
    /// changed, since saving now writes different bytes.
    /// </summary>
    public void SwitchEncoding(bool backwards = false)
    {
        if (!FileToTextConverterService.IsEncodingRelevant(Url))
        {
            return;
        }

        var model = TextEditorControl.TextModel;
        TextEncodings.Reinterpret(model, TextEncodings.Next(model.Encoding, backwards));
        TextEditorControl.IsDirty = true;
        TextEditorControl.Refresh();
        StateHasChanged();
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
