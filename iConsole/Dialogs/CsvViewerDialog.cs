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
/// F8 / Shift+F8 read the grid again under the next / previous code page (the title names the current one); the F4 text
/// view always picks its own code page, the way the F3 viewer does, and ignores the switch.
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
    private byte[]? _bytes;
    private int _bomLength;
    private Encoding? _encoding;
    private int _documentGeneration;
    private int _pendingDocumentGeneration;
    private Task<CsvDocument>? _switch;

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

    /// <summary>
    /// The code page the grid's text was read with: what <see cref="TextEncodings.Decode"/> picked, then what F8 chose. Null while
    /// the file loads or when there is nothing to read.
    /// </summary>
    public Encoding? Encoding => _encoding;

    /// <summary>The re-read that an F8 started, or null when none is running. The grid changes on the next idle pass.</summary>
    public Task<CsvDocument>? SwitchTask => _switch;

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

        string text = TextEncodings.Decode(content, out Encoding encoding);
        // Kept together, so an F8 pressed while the file parses sees the bytes and the code page they were read with.
        _bytes = content;
        _bomLength = BomLength(content);
        _encoding = encoding;
        Title = TitleText();
        int generation = ++_documentGeneration;
        _pendingDocumentGeneration = generation;
        char? delimiter = DelimiterFor(Url);
        CsvDocument document = await Task.Run(() => CsvDocument.Parse(text, delimiter));
        if (generation != _documentGeneration)
        {
            // An F8 came in while the file parsed; its re-read lands the document.
            return;
        }

        _document = document;
        _grid.SetData(document.Headers, document.Rows);
        _matched = document.Rows.Count;
        // The grid already shows every row, which is what this query would give; the line says so and is ready to edit.
        _queryBox.Text = DefaultQuery;
        UpdateStatus();
        StateHasChanged();
    }

    /// <summary>The query the line starts with: every column of every row.</summary>
    public const string DefaultQuery = "SELECT * FROM file";

    /// <summary>
    /// The file's text: a byte order mark decides, else UTF-8 when the bytes are valid UTF-8, else the system's ANSI code page
    /// (see <see cref="TextEncodings.Decode"/>). Never lossy: a Windows-1250 file is no longer a string of replacement characters.
    /// </summary>
    public static string Decode(byte[] content) => TextEncodings.Decode(content, out _);

    /// <summary>A .tsv file is tab-separated whatever its first line looks like: a comma in a value must not make it a comma file.</summary>
    private static char? DelimiterFor(Url url) =>
        string.Equals(Path.GetExtension(url.Path), ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : null;

    private static int BomLength(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return 3;
        }

        return content.Length >= 2 && ((content[0] == 0xFF && content[1] == 0xFE) || (content[0] == 0xFE && content[1] == 0xFF)) ? 2 : 0;
    }

    /// <summary>
    /// F8 / Shift+F8: reads the file again under the next / previous code page. The code page changes now, so quick presses keep
    /// advancing; the grid changes on the next idle pass. A query that was running belongs to the old reading and is dropped.
    /// Nothing happens while the file has not been read, or while the file's text is showing.
    /// </summary>
    public void SwitchEncoding(bool backwards = false)
    {
        if (_bytes is null || _encoding is null || _textMode || Url is null)
        {
            return;
        }

        byte[] bytes = _bytes;
        int bomLength = _bomLength;
        Encoding encoding = TextEncodings.Next(_encoding, backwards);
        char? delimiter = DelimiterFor(Url);
        _encoding = encoding;
        _query = null;
        int generation = ++_documentGeneration;
        _pendingDocumentGeneration = generation;
        _switch = Task.Run(() => CsvDocument.Parse(encoding.GetString(bytes, bomLength, bytes.Length - bomLength), delimiter));
        Title = TitleText();
        StateHasChanged();
    }

    /// <summary>Starts running a query over the file. The grid changes on the next idle pass; nothing here waits for it.</summary>
    public void StartQuery(string text)
    {
        if (_document is null || _switch is not null)
        {
            // While a re-read is running the document on hand is the old one; its rows would land on top of the new reading.
            return;
        }

        CsvDocument document = _document;
        _pendingGeneration = ++_generation;
        _query = Task.Run(() => CsvQuery.Run(document, text));
    }

    /// <summary>Puts a finished query on screen. A query that was overtaken by a newer one is dropped, as is one that failed to parse, which keeps the old rows.</summary>
    public override void OnIdle()
    {
        if (_switch is { IsCompleted: true } reread && _pendingDocumentGeneration == _documentGeneration)
        {
            _switch = null;
            if (reread.Exception is { } failure)
            {
                _queryError = failure.InnerException?.Message ?? failure.Message;
            }
            else
            {
                _document = reread.Result;
                _queryError = string.Empty;
                _matched = _document.Rows.Count;
                _grid.SetData(_document.Headers, _document.Rows);
            }

            UpdateStatus();
            StateHasChanged();
        }

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

    private string TitleText()
    {
        string encoding = !_textMode && _encoding is not null ? $"  [{TextEncodings.DisplayName(_encoding)}]" : string.Empty;
        return (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length - encoding.Length)) ?? "CSV")
            + (_textMode ? " [TEXT]" : "  F4:Text  Enter:query  Tab:grid" + encoding);
    }

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

        if (keyEvent.ConsoleKey == ConsoleKey.F8 && !keyEvent.Control && !keyEvent.Alt)
        {
            // Always handled: F8 is Delete in the panels and Shift+F8 opens the settings, neither of which belongs behind this dialog.
            SwitchEncoding(backwards: keyEvent.Shift);
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
        yield return ("F8", UiText.Get("Viewer.HintCodePage", "Next code page (Shift: previous)"));
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
