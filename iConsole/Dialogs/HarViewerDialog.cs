using System.Globalization;
using System.Text.Json;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Har;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The HAR viewer: a browser's network log as a grid, one row per request (number, method, status, type, size, time, URL).
/// Enter opens the row's request and response: the headers, the query string, the timings and both bodies, with a JSON body
/// pretty-printed. Backspace goes back to the list. F4 shows the file's own text, and F4 again goes back to the list.
///
/// <para>The file is read and parsed on a worker thread, so a large log never holds up a key.</para>
/// </summary>
public class HarViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private enum ViewMode
    {
        Entries,
        Details,
        Text,
    }

    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly CsvGrid _grid;
    private readonly TextViewerControl _detailsControl;
    private readonly TextViewerControl _textControl;
    private readonly Label _statusLabel;
    private HarDocument? _document;
    private ViewMode _mode;
    private bool _textLoaded;

    public HarViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IThemeService themeService)
        : base(nameof(HarViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Har.Grid.Empty", "The file has no requests") };
        _detailsControl = new TextViewerControl(false, themeService) { IsVisible = false };
        _textControl = new TextViewerControl(false, themeService) { IsVisible = false };
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_grid);
        AddControl(_detailsControl);
        AddControl(_textControl);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown.</summary>
    public Url? Url { get; set; }

    /// <summary>The parsed file, or null while it loads or when it is not a HAR file.</summary>
    public HarDocument? Document => _document;

    /// <summary>The grid of requests, for a test or a caller to read the rows on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>True while one request's details are shown.</summary>
    public bool IsShowingDetails => _mode == ViewMode.Details;

    /// <summary>True while the file's own text is shown.</summary>
    public bool IsTextMode => _mode == ViewMode.Text;

    /// <summary>The details text the request view shows; empty until a row is opened.</summary>
    public string[] DetailLines => _detailsControl.TextModel.Lines.AsArray();

    /// <summary>What the status line says: the request count, or why the file could not be shown.</summary>
    public string Status => _statusLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IFileSystem? fileSystem = (_contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel()) as IFileSystem;
        if (Url is null || fileSystem is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        byte[]? content = (await fileSystem.ReadFileAsync(Url))?.Content;
        if (content is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.CouldNotRead", "Could not read the file");
            return;
        }

        Title = TitleText();
        try
        {
            HarDocument document = await Task.Run(() => HarDocument.Parse(TextEncodings.Decode(content, out _)));
            _document = document;
            _grid.SetData(Headers(), document.Entries.Select(Cells).ToList());
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException)
        {
            _statusLabel.Text = string.Format(UiText.Get("Har.Status.NotHar", "Not a HAR file: {0}"), failure.Message);
            StateHasChanged();
            return;
        }

        UpdateStatus();
        StateHasChanged();
    }

    /// <summary>Opens the row under the cursor: one request and its response.</summary>
    public void ShowDetails()
    {
        if (_document is null || _grid.RowCount == 0)
        {
            return;
        }

        HarEntry entry = _document.Entries[_grid.CursorRow];
        _detailsControl.TextModel.Lines.ReplaceAll(HarEntryDetails.Build(entry));
        _detailsControl.TextModel.TriggerPropertyChanged();
        SetMode(ViewMode.Details);
    }

    private async Task ShowTextAsync()
    {
        if (!_textLoaded)
        {
            IFileSystem? fileSystem = (_contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel()) as IFileSystem;
            await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, _textControl.TextModel);
            _textLoaded = true;
        }

        SetMode(ViewMode.Text);
    }

    private void SetMode(ViewMode mode)
    {
        _mode = mode;
        _grid.IsVisible = mode == ViewMode.Entries;
        _detailsControl.IsVisible = mode == ViewMode.Details;
        _textControl.IsVisible = mode == ViewMode.Text;
        switch (mode)
        {
            case ViewMode.Details:
                _detailsControl.SetFocus();
                break;
            case ViewMode.Text:
                _textControl.SetFocus();
                break;
            default:
                _grid.SetFocus();
                break;
        }

        Title = TitleText();
        StateHasChanged();
    }

    private static string[] Headers() =>
    [
        UiText.Get("Har.Column.Number", "#"),
        UiText.Get("Har.Column.Method", "Method"),
        UiText.Get("Har.Column.Status", "Status"),
        UiText.Get("Har.Column.Type", "Type"),
        UiText.Get("Har.Column.Size", "Size"),
        UiText.Get("Har.Column.Time", "Time (ms)"),
        UiText.Get("Har.Column.Url", "URL"),
    ];

    private static string[] Cells(HarEntry entry) =>
    [
        entry.Number.ToString(CultureInfo.InvariantCulture),
        entry.Method,
        entry.Status > 0 ? entry.Status.ToString(CultureInfo.InvariantCulture) : "-",
        TypeOf(entry.MimeType),
        entry.Size is { } size ? size.ToString("N0", CultureInfo.CurrentCulture) : "-",
        entry.TimeMs.ToString("N1", CultureInfo.CurrentCulture),
        entry.Url,
    ];

    /// <summary>The media type without its parameters (<c>text/html; charset=utf-8</c> shows as <c>text/html</c>).</summary>
    private static string TypeOf(string mimeType)
    {
        int semicolon = mimeType.IndexOf(';');
        string type = (semicolon >= 0 ? mimeType[..semicolon] : mimeType).Trim();
        return type.Length == 0 ? "-" : type;
    }

    private void UpdateStatus()
    {
        if (_document is null)
        {
            return;
        }

        if (_document.Entries.Count == 0)
        {
            _statusLabel.Text = UiText.Get("Har.Grid.Empty", "The file has no requests");
            return;
        }

        string status = string.Format(UiText.Get("Har.Status.Requests", "{0:N0} request(s)"), _document.Entries.Count);
        if (_document.Truncated)
        {
            status += string.Format(UiText.Get("Har.Status.Truncated", "; only the first {0:N0} are read"), HarDocument.MaxEntries);
        }

        _statusLabel.Text = status;
    }

    private string TitleText()
    {
        if (_mode == ViewMode.Text)
        {
            return (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length - " [TEXT]".Length)) ?? "HAR") + " [TEXT]";
        }

        if (_mode == ViewMode.Details && _grid.RowCount > 0 && _document is not null)
        {
            string details = string.Format(UiText.Get("Har.Title.Details", "Request {0}"), _document.Entries[_grid.CursorRow].Number);
            return details + "  Backspace:list";
        }

        return (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length - "  Enter:details  F4:Text".Length)) ?? "HAR")
            + "  Enter:details  F4:Text";
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the details and the text are shown on demand.
        _mode = ViewMode.Entries;
        _detailsControl.IsVisible = false;
        _textControl.IsVisible = false;
        Title = TitleText();
        _grid.SetFocus();
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        bool plain = !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt;
        if (plain && keyEvent.ConsoleKey == ConsoleKey.Enter && _mode == ViewMode.Entries)
        {
            ShowDetails();
            return true;
        }

        if (plain && keyEvent.ConsoleKey == ConsoleKey.Backspace && _mode != ViewMode.Entries)
        {
            SetMode(ViewMode.Entries);
            return true;
        }

        if (plain && keyEvent.ConsoleKey == ConsoleKey.F4)
        {
            if (_mode == ViewMode.Text)
            {
                SetMode(ViewMode.Entries);
            }
            else
            {
                await ShowTextAsync();
            }

            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The keys this dialog adds to the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Har.Hint.Details", "Show the request and its response"));
        yield return ("Backspace", UiText.Get("Har.Hint.Back", "Back to the list of requests"));
        yield return ("F4", UiText.Get("Viewer.HintToggleText", "Show the file's text, or the grid again"));
    }

    private sealed class CloseSelfCommand(HarViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = TitleText();
        }

        // Inside the border: the body, and the status line above the bottom border. A terminal too small gets empty rows.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int bodyHeight = Math.Max(1, (int)ViewSize.Height - 3);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _grid.Position = new Position(1, 1);
        _grid.ViewSize = new Size(width, bodyHeight);

        _detailsControl.Position = new Position(1, 1);
        _detailsControl.ViewSize = new Size(width, bodyHeight);

        _textControl.Position = new Position(1, 1);
        _textControl.ViewSize = new Size(width, bodyHeight);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
