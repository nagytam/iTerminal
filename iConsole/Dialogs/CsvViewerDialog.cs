using System.Text;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Csv;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The CSV viewer: the file as a grid, with the header on top and the columns scrolling sideways. The query line above the grid
/// takes a small SQL-like query (see <see cref="CsvQuery"/>): <c>SELECT name, amount WHERE amount &gt; 10 ORDER BY amount DESC</c>.
/// Enter runs it; the grid then shows its rows and the status line says how many matched. F4 switches to the file's text.
///
/// <para>A query runs on a background task, and its result is put in the grid on the next idle pass, so a large file never
/// holds up a key. Only the newest query may land.</para>
/// </summary>
public class CsvViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly TextBox _queryBox;
    private readonly CsvGrid _grid;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private CsvDocument? _document;
    private bool _textMode;
    private bool _textLoaded;
    private int _generation;
    private int _pendingGeneration;
    private Task<CsvQueryResult>? _query;
    private string _queryError = string.Empty;
    private int _matched;

    public CsvViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(CsvViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        // The query line is added first: a dialog gives the keyboard to its first focusable control, and the query is where a person starts.
        _queryBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Csv.Query.Label", "Query"),
            Placeholder = UiText.Get("Csv.Query.Placeholder", "e.g. SELECT name, amount WHERE amount > 10 ORDER BY amount DESC LIMIT 20"),
        };
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Csv.Grid.Empty", "No rows match the query") };
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_queryBox);
        AddControl(_grid);
        AddControl(_textViewerControl);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown.</summary>
    public Url? Url { get; set; }

    /// <summary>The file as it was read, or null while it loads or when it could not be read.</summary>
    public CsvDocument? Document => _document;

    /// <summary>The grid, for a test or a caller to read the rows on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>True while the file's text is shown instead of the grid (F4 toggles).</summary>
    public bool IsTextMode => _textMode;

    /// <summary>The query that the grid's rows came from, or null while the query is still running.</summary>
    public Task<CsvQueryResult>? QueryTask => _query;

    /// <summary>What the status line says: the row count, the query's error, or why the file could not be shown.</summary>
    public string Status => _statusLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
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

        string text = Decode(content);
        // A .tsv file is tab-separated whatever its first line looks like: a comma in a value must not make it a comma file.
        char? delimiter = string.Equals(Path.GetExtension(Url.Path), ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : null;
        CsvDocument document = await Task.Run(() => CsvDocument.Parse(text, delimiter));
        _document = document;
        _grid.SetData(document.Headers, document.Rows);
        _matched = document.Rows.Count;
        UpdateStatus();
        StateHasChanged();
    }

    /// <summary>
    /// The file's text: UTF-16 when it starts with that byte order mark (what Excel's "Unicode Text" saves), otherwise UTF-8.
    /// Read as UTF-8, a UTF-16 file is a string of unrelated characters and nulls.
    /// </summary>
    public static string Decode(byte[] content)
    {
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
        }

        return Encoding.UTF8.GetString(content);
    }

    /// <summary>Starts running a query over the file. The grid changes on the next idle pass; nothing here waits for it.</summary>
    public void StartQuery(string text)
    {
        if (_document is null)
        {
            return;
        }

        CsvDocument document = _document;
        _pendingGeneration = ++_generation;
        _query = Task.Run(() => CsvQuery.Run(document, text));
    }

    /// <summary>Puts a finished query on screen. A query that was overtaken by a newer one is dropped, as is one that failed to parse, which keeps the old rows.</summary>
    public override void OnIdle()
    {
        if (_query is { IsCompleted: true } query && _pendingGeneration == _generation)
        {
            _query = null;
            if (query.Exception is { } failure)
            {
                _queryError = failure.InnerException?.Message ?? failure.Message;
            }
            else
            {
                CsvQueryResult result = query.Result;
                _queryError = string.Empty;
                _matched = result.Matched;
                _grid.SetData(result.Headers, result.Rows);
            }

            UpdateStatus();
            StateHasChanged();
        }

        base.OnIdle();
    }

    private void UpdateStatus()
    {
        if (_queryError.Length > 0)
        {
            _statusLabel.Text = _queryError;
            return;
        }

        if (_document is null)
        {
            return;
        }

        if (_document.Headers.Count == 0)
        {
            _statusLabel.Text = UiText.Get("Csv.Status.Empty", "The file is empty");
            return;
        }

        string status = string.Format(UiText.Get("Csv.Status.Rows", "{0:N0} of {1:N0} row(s), {2} column(s)"), _matched, _document.Rows.Count, _grid.ColumnCount);
        if (_document.Truncated)
        {
            status += string.Format(UiText.Get("Csv.Status.Truncated", "; only the first {0:N0} rows are read"), CsvDocument.MaxRows);
        }

        _statusLabel.Text = status;
    }

    /// <summary>Switches between the grid and the file's text.</summary>
    public async Task ToggleTextModeAsync()
    {
        if (!_textMode)
        {
            await ShowTextAsync();
        }
        else
        {
            SetMode(textMode: false);
        }
    }

    private async Task ShowTextAsync()
    {
        if (!_textLoaded)
        {
            IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
            await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, _textViewerControl.TextModel);
            _textLoaded = true;
        }

        SetMode(textMode: true);
    }

    private void SetMode(bool textMode)
    {
        _textMode = textMode;
        _queryBox.IsVisible = !textMode;
        _grid.IsVisible = !textMode;
        _textViewerControl.IsVisible = textMode;
        if (textMode)
        {
            _textViewerControl.SetFocus();
        }
        else
        {
            _queryBox.SetFocus();
        }

        Title = TitleText();
        StateHasChanged();
    }

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "CSV")
        + (_textMode ? " [TEXT]" : "  F4:Text  Enter:query  Tab:grid");

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the text view is on demand.
        _textMode = false;
        _textViewerControl.IsVisible = false;
        _queryBox.SetFocus();
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

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt && !_textMode)
        {
            StartQuery(_queryBox.Text);
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F4 && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            await ToggleTextModeAsync();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The keys this dialog adds to the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Csv.Hint.RunQuery", "Run the query over the rows"));
        yield return ("F4", UiText.Get("Viewer.HintToggleText", "Show the file's text, or the grid again"));
    }

    private sealed class CloseSelfCommand(CsvViewerDialog owner) : Command
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

        // Inside the border: the query line, the grid, and the status line above the bottom border. A terminal too small for
        // them gets empty rows, not negative sizes.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int gridHeight = Math.Max(1, (int)ViewSize.Height - 5);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _queryBox.Position = new Position(1, 1);
        _queryBox.ViewSize = new Size(width, 1);

        _grid.Position = new Position(1, 2);
        _grid.ViewSize = new Size(width, gridHeight);

        _textViewerControl.Position = new Position(1, 1);
        _textViewerControl.ViewSize = new Size(width, Math.Max(1, (int)ViewSize.Height - 3));

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
