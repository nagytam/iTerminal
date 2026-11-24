using iConsole.Buffers;
using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Scanning;
using SkiaSharp;

namespace iTerminal.Core.Dialogs.Scanning;

/// <summary>
/// Scan a page or the whole document feeder, look at it, and save it.
///
/// <list type="bullet">
/// <item><b>Device selection.</b> Up/Down choose the scanner; F5 reads the list again. The list is read on a background
/// task, so the dialog opens at once.</item>
/// <item><b>Settings.</b> <c>D</c> switches between the flatbed (one page) and the feeder (every sheet, ADF); <c>+</c>/<c>-</c>
/// the resolution; <c>C</c> the colour mode; <c>F</c> the file format saved (PNG or JPEG). Each setting shows its key
/// beside its value.</item>
/// <item><b>Scan.</b> Enter or <c>S</c> scans in the background. While it runs a bar moves and the elapsed time counts up;
/// Esc cancels it. The file names are fixed when the scan lands, so the dialog shows what <c>W</c> will write.</item>
/// <item><b>Preview.</b> The page is drawn as characters with its pixel size. <c>[</c> and <c>]</c> step through the pages
/// of a feeder run. The picture is decoded once, on a background task, when the scan lands.</item>
/// <item><b>Save.</b> <c>W</c> writes every page to the folder the dialog was opened on, as <c>scan-yyyyMMdd-HHmmss.png</c>
/// (with <c>-001</c>… added when there is more than one page). A name already in the folder is not overwritten: the stem
/// gets <c>-2</c>, <c>-3</c>… instead. The status line says which page is being written and where the pages went.</item>
/// </list>
/// </summary>
public class ScannerDialog : Dialog
{
    private const int DialogWidth = 84;
    private const int DialogHeight = 32;
    private const int ListRows = 4;
    private const int BarMillisecondsPerStep = 90;
    private const int BarWidth = 24;

    private readonly IScanService _scanService;
    private readonly Lock _sync = new();

    private CancellationTokenSource _cancellation = new();
    private IReadOnlyList<ScannerDevice> _scanners = [];
    private int _scannerIndex;
    private bool _scannersLoading;
    private string? _listFailure;

    private ScanOptions _options = ScanOptions.Default;
    private ScanFileFormat _format = ScanFileFormat.Png;
    private string? _folder;

    private bool _scanning;
    private bool _saving;
    private bool _scanFeeder;
    private long _scanStartedAt;
    private int _generation;
    private List<ScanPage> _pages = [];
    private List<SKBitmap?> _bitmaps = [];
    private int _pageIndex;

    // The stem of the file names ("scan-20261121-101530"), fixed when a scan lands; _savedCount is how many pages of
    // the current scan are on disk under it.
    private string _fileStem = string.Empty;
    private int _savedCount;

    private string _status = string.Empty;
    private bool _statusIsError;
    private Task _pending = Task.CompletedTask;

    public ScannerDialog(IScanService scanService, IThemeService themeService)
        : base(nameof(ScannerDialog), themeService)
    {
        Title = UiText.Get("Menu.Scanner", "Scan");
        _scanService = scanService;
    }

    /// <summary>The task the last scan, save or list started; nothing waits on it, it exists so a test can.</summary>
    public Task PendingOperation
    {
        get { lock (_sync) return _pending; }
    }

    /// <summary>Where <c>W</c> saves to: the folder the dialog was opened on. Null when that is not a local folder.</summary>
    public void Load(string? folder)
    {
        lock (_sync)
        {
            _folder = folder;
            _status = string.Empty;
            _statusIsError = false;
        }
    }

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
        }

        StartLoadingScanners(_cancellation.Token);
        await base.ShowAsync();
    }

    public override void Close()
    {
        lock (_sync)
        {
            // A scan or a decode still in flight must not land on a closed dialog.
            _generation++;
            _cancellation.Cancel();
            DisposeBitmaps();
        }

        base.Close();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Min(DialogWidth, Math.Max(40, (int)Parent.ViewSize.Width - 2)), Math.Min(DialogHeight, (int)Parent.ViewSize.Height - 2)));
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        bool busy;
        lock (_sync)
        {
            busy = _scanning;
        }

        if (busy)
        {
            yield return ("Esc", UiText.Get("DialogHint.CancelScan", "Cancel the scan"));
            yield break;
        }

        yield return ("Enter / S", UiText.Get("DialogHint.Scan", "Scan"));
        yield return ("Up/Down", UiText.Get("DialogHint.ChooseScanner", "Choose the scanner"));
        yield return ("D", UiText.Get("DialogHint.SourceToggle", "Flatbed or feeder"));
        yield return ("+ / -", UiText.Get("DialogHint.Resolution", "Resolution"));
        yield return ("C", UiText.Get("DialogHint.ColorMode", "Colour mode"));
        yield return ("F", UiText.Get("DialogHint.FileFormat", "File format"));
        yield return ("[ / ]", UiText.Get("DialogHint.PageStep", "Previous or next page"));
        yield return ("W", UiText.Get("DialogHint.Save", "Save the pages"));
        yield return ("F5", UiText.Get("DialogHint.RefreshScanners", "Read the scanners again"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (HandleKey(keyEvent))
        {
            StateHasChanged();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private bool HandleKey(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt;
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            // Esc cancels a running scan first; a second Esc (not handled here) closes the dialog.
            lock (_sync)
            {
                if (!_scanning)
                {
                    return false;
                }

                _cancellation.Cancel();
                return true;
            }
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F5 && plain)
        {
            StartLoadingScanners(CancellationToken.None);
            return true;
        }

        if (!plain)
        {
            return false;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter:
                StartScan();
                return true;
            case ConsoleKey.UpArrow:
                return ChooseScanner(-1);
            case ConsoleKey.DownArrow:
                return ChooseScanner(1);
        }

        char key = char.ToLowerInvariant(keyEvent.KeyChar);
        switch (key)
        {
            case 's':
                StartScan();
                return true;
            case 'd':
                ToggleSource();
                return true;
            case '+':
            case '=':
                ChangeResolution(1);
                return true;
            case '-':
                ChangeResolution(-1);
                return true;
            case 'c':
                CycleColorMode();
                return true;
            case 'f':
                ToggleFormat();
                return true;
            case '[':
                return StepPage(-1);
            case ']':
                return StepPage(1);
            case 'w':
                StartSave();
                return true;
        }

        return false;
    }

    #region Scanners

    private void StartLoadingScanners(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _scannersLoading = true;
        }

        Task task = Task.Run(async () =>
        {
            ScannerListing listing;
            try
            {
                listing = await _scanService.ListScannersAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_sync)
            {
                _scanners = listing.Scanners;
                _listFailure = listing.Failure;
                _scannerIndex = Math.Min(_scannerIndex, Math.Max(0, _scanners.Count - 1));
                _scannersLoading = false;
            }

            StateHasChanged();
        }, cancellationToken);

        lock (_sync)
        {
            _pending = task;
        }
    }

    private bool ChooseScanner(int step)
    {
        lock (_sync)
        {
            if (_scanners.Count == 0)
            {
                return true;
            }

            _scannerIndex = Math.Clamp(_scannerIndex + step, 0, _scanners.Count - 1);
            return true;
        }
    }

    #endregion

    #region Settings

    private void ToggleSource()
    {
        lock (_sync)
        {
            _options = _options with { Source = _options.Source == ScanSource.Flatbed ? ScanSource.Feeder : ScanSource.Flatbed };
        }
    }

    private void ChangeResolution(int step)
    {
        lock (_sync)
        {
            int index = Array.IndexOf(ScanOptions.Resolutions, _options.Dpi);
            index = Math.Clamp((index < 0 ? 1 : index) + step, 0, ScanOptions.Resolutions.Length - 1);
            _options = _options with { Dpi = ScanOptions.Resolutions[index] };
        }
    }

    private void CycleColorMode()
    {
        lock (_sync)
        {
            ScanColorMode next = _options.ColorMode switch
            {
                ScanColorMode.Color => ScanColorMode.Grayscale,
                ScanColorMode.Grayscale => ScanColorMode.BlackAndWhite,
                _ => ScanColorMode.Color,
            };
            _options = _options with { ColorMode = next };
        }
    }

    private void ToggleFormat()
    {
        lock (_sync)
        {
            _format = _format == ScanFileFormat.Png ? ScanFileFormat.Jpeg : ScanFileFormat.Png;
        }
    }

    #endregion

    #region Scan and save

    private void StartScan()
    {
        lock (_sync)
        {
            if (_scanning || _saving)
            {
                SetStatus(UiText.Get("Scan.AlreadyRunning", "A scan is running. Esc cancels it."), false);
                return;
            }

            if (_scannersLoading)
            {
                return;
            }

            if (_scanners.Count == 0)
            {
                SetStatus(_listFailure ?? UiText.Get("Scan.NoScanner", "No scanner is selected."), true);
                return;
            }

            ScannerDevice scanner = _scanners[_scannerIndex];
            ScanOptions options = _options;
            int generation = ++_generation;
            _scanning = true;
            _scanFeeder = options.Source == ScanSource.Feeder;
            _scanStartedAt = Environment.TickCount64;
            SetStatus(UiText.Get("Scan.Scanning", "Scanning…"), false);
            CancellationToken token = _cancellation.Token;

            _pending = Task.Run(async () =>
            {
                ScanResult result;
                try
                {
                    result = await _scanService.ScanAsync(scanner, options, token);
                }
                catch (OperationCanceledException)
                {
                    result = ScanResult.Failed(UiText.Get("Scan.Cancelled", "The scan was cancelled."));
                }
                catch (Exception exception)
                {
                    result = ScanResult.Failed(exception.Message);
                }

                AcceptResult(generation, result);
            }, CancellationToken.None);
        }
    }

    private void AcceptResult(int generation, ScanResult result)
    {
        lock (_sync)
        {
            if (generation != _generation)
            {
                return;
            }

            _scanning = false;
            DisposeBitmaps();
            _pages = result.Success ? [.. result.Pages] : [];
            _pageIndex = 0;
            _savedCount = 0;
            _fileStem = "scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            SetStatus(result.Message, !result.Success);
            _bitmaps = _pages.Select(_ => (SKBitmap?)null).ToList();
        }

        // Decoding a large page takes long; it runs here, off the render thread, and lands only if nothing changed meanwhile.
        List<ScanPage> decode;
        lock (_sync)
        {
            decode = [.. _pages];
        }

        var decoded = decode.Select(ScanImageCodec.Decode).ToList();
        lock (_sync)
        {
            if (generation != _generation)
            {
                foreach (SKBitmap? bitmap in decoded)
                {
                    bitmap?.Dispose();
                }

                return;
            }

            _bitmaps = decoded;
        }

        StateHasChanged();
    }

    private void StartSave()
    {
        string? folder;
        List<ScanPage> pages;
        ScanFileFormat format;
        string stem;
        lock (_sync)
        {
            folder = _folder;
            pages = [.. _pages];
            format = _format;
            stem = _fileStem;
            if (_scanning || _saving)
            {
                return;
            }

            if (pages.Count == 0)
            {
                SetStatus(UiText.Get("Scan.NothingToSave", "There is no scanned page to save."), true);
                return;
            }

            if (folder is null || !Directory.Exists(folder))
            {
                SetStatus(UiText.Get("Scan.NoFolder", "Open the dialog on a local folder to save there."), true);
                return;
            }

            _saving = true;
        }

        string extension = format == ScanFileFormat.Jpeg ? ".jpg" : ".png";
        IScanService service = _scanService;
        _pending = Task.Run(async () =>
        {
            string target = UniqueStem(folder, stem, extension, pages.Count);
            int written = 0;
            string? failure = null;
            for (int i = 0; i < pages.Count; i++)
            {
                lock (_sync)
                {
                    SetStatus(string.Format(UiText.Get("Scan.SavingPage", "Saving page {0} of {1}…"), i + 1, pages.Count), false);
                }

                SaveResult saved = await service.SaveAsync(pages[i], Path.Combine(folder, FileName(target, i, pages.Count, extension)), format, CancellationToken.None);
                if (!saved.Success)
                {
                    failure = saved.Message;
                    lock (_sync)
                    {
                        SetStatus(string.Format(UiText.Get("Scan.SaveFailed", "Page {0} of {1} was not saved: {2}"), i + 1, pages.Count, saved.Message), true);
                    }

                    break;
                }

                written = i + 1;
            }

            lock (_sync)
            {
                _saving = false;
                _fileStem = target;
                _savedCount = written;
                if (failure is null)
                {
                    SetStatus(string.Format(UiText.Get("Scan.Saved", "Saved to {0}."), folder), false);
                }
            }

            StateHasChanged();
        }, CancellationToken.None);
    }

    /// <summary>
    /// The stem itself, or the stem with <c>-2</c>, <c>-3</c>… added while one of the names it would write already exists,
    /// so a save in the same second as the last one does not overwrite it.
    /// </summary>
    private static string UniqueStem(string folder, string stem, string extension, int count)
    {
        for (int attempt = 1; ; attempt++)
        {
            string candidate = attempt == 1 ? stem : $"{stem}-{attempt}";
            bool taken = Enumerable.Range(0, count).Any(i => File.Exists(Path.Combine(folder, FileName(candidate, i, count, extension))));
            if (!taken)
            {
                return candidate;
            }
        }
    }

    private static string FileName(string stem, int index, int count, string extension) =>
        count == 1 ? stem + extension : $"{stem}-{index + 1:000}{extension}";

    private bool StepPage(int step)
    {
        lock (_sync)
        {
            if (_pages.Count == 0)
            {
                return true;
            }

            _pageIndex = Math.Clamp(_pageIndex + step, 0, _pages.Count - 1);
            return true;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    private void DisposeBitmaps()
    {
        foreach (SKBitmap? bitmap in _bitmaps)
        {
            bitmap?.Dispose();
        }

        _bitmaps = [];
    }

    #endregion

    #region Drawing

    /// <summary>
    /// The layout, top to bottom, inside the border: the scanner list, the settings and the save line, the page caption,
    /// the preview, the progress line, the status line, and two lines of keys.
    /// </summary>
    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 8 || ih <= 10)
        {
            return;
        }

        ColorScheme colors = ThemeService.CurrentColorScheme;
        lock (_sync)
        {
            for (int row = 0; row < ih; row++)
            {
                Put(buffer, 0, row, iw, string.Empty, colors.Foreground, colors.Background);
            }

            int textWidth = iw - 2;
            DrawScanners(buffer, colors, textWidth);

            const int settingsRow = 1 + ListRows + 1;
            const int folderRow = settingsRow + 1;
            const int fileRow = folderRow + 1;
            const int captionRow = fileRow + 1;
            int previewTop = captionRow + 1;
            int progressRow = ih - 5;
            int statusRow = ih - 4;
            int actionRow = ih - 2;
            int navigationRow = ih - 1;

            Put(buffer, 1, settingsRow, textWidth, SettingsLine(), colors.Foreground, colors.Background);
            Put(buffer, 1, folderRow, textWidth, SaveToLine(textWidth), colors.Foreground, colors.Background);
            Put(buffer, 1, fileRow, textWidth, FileLine(), colors.Foreground, colors.Background);
            Put(buffer, 1, captionRow, textWidth, PageCaption(), colors.Foreground, colors.Background);

            DrawPreview(buffer, colors, previewTop, progressRow - 1 - previewTop, textWidth);

            if (_scanning)
            {
                long elapsedMilliseconds = Math.Max(0, Environment.TickCount64 - _scanStartedAt);
                string bar = ProgressBar.BuildIndeterminateBar(BarWidth, (int)(elapsedMilliseconds / BarMillisecondsPerStep));
                string elapsed = TimeSpan.FromMilliseconds(elapsedMilliseconds).ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
                string feeder = _scanFeeder ? "  " + UiText.Get("Scan.FeederWait", "The feeder's pages arrive when it is empty.") : string.Empty;
                Put(buffer, 1, progressRow, textWidth, $"{bar}  {elapsed}{feeder}", colors.Foreground, colors.Background);
            }

            Put(buffer, 1, statusRow, textWidth, _status, _statusIsError ? colors.Error : colors.Foreground, colors.Background);

            // The action keys come first, so a narrow dialog drops the navigation hints and keeps the way out.
            string actions = _scanning
                ? $"Esc:{UiText.Get("DialogHint.CancelScan", "Cancel the scan")}"
                : string.Join("  ", new[]
                {
                    $"Enter/S:{UiText.Get("DialogHint.Scan", "Scan")}",
                    $"W:{UiText.Get("DialogHint.Save", "Save the pages")}",
                    $"F5:{UiText.Get("DialogHint.RefreshScanners", "Read the scanners again")}",
                    UiText.Get("Hint.Part.EscClose", "Esc:close"),
                });
            Put(buffer, 1, actionRow, textWidth, actions.FitHints(textWidth), colors.Foreground, colors.Background);

            if (!_scanning)
            {
                string navigation = string.Join("  ", new[]
                {
                    $"Up/Down:{UiText.Get("DialogHint.ChooseScanner", "Choose the scanner")}",
                    $"[ / ]:{UiText.Get("DialogHint.PageStep", "Previous or next page")}",
                });
                Put(buffer, 1, navigationRow, textWidth, navigation.FitHints(textWidth), colors.Foreground, colors.Background);
            }
        }
    }

    private void DrawScanners(IBuffer buffer, ColorScheme colors, int textWidth)
    {
        int listRows = Math.Min(ListRows, Math.Max(1, _scanners.Count));
        if (_scanners.Count == 0)
        {
            string text = _scannersLoading
                ? UiText.Get("Scan.Searching", "Looking for scanners…")
                : _listFailure ?? UiText.Get("Scan.NoScanners", "No scanner found.");
            Put(buffer, 1, 1, textWidth, text, colors.Foreground, colors.Background);
            return;
        }

        int first = Math.Max(0, Math.Min(_scannerIndex - listRows + 1, _scanners.Count - listRows));
        for (int i = 0; i < listRows; i++)
        {
            int index = first + i;
            ScannerDevice device = _scanners[index];
            bool chosen = index == _scannerIndex;
            string marker = chosen ? "> " : "  ";
            Put(buffer, 1, 1 + i, textWidth, marker + device.Name, chosen ? Color.Cyan : colors.Foreground, colors.Background);
        }
    }

    private void DrawPreview(IBuffer buffer, ColorScheme colors, int top, int rows, int columns)
    {
        if (rows <= 0 || columns <= 0)
        {
            return;
        }

        SKBitmap? bitmap = _pageIndex < _bitmaps.Count ? _bitmaps[_pageIndex] : null;
        if (bitmap is not null)
        {
            IReadOnlyList<string> lines = ScanImageCodec.AsciiPreview(bitmap, columns, rows);
            for (int i = 0; i < lines.Count && i < rows; i++)
            {
                Put(buffer, 1, top + i, columns, lines[i], colors.Foreground, colors.Background);
            }

            return;
        }

        // While a scan runs the progress line and the status line say so; the preview waits for the page.
        if (!_scanning)
        {
            Put(buffer, 1, top, columns, UiText.Get("Scan.Empty", "Choose a scanner and press Enter to scan."), colors.Foreground, colors.Background);
        }
    }

    /// <summary>The settings, each with the key that changes it: <c>D</c> source, <c>+ / -</c> resolution, <c>C</c> colour, <c>F</c> format.</summary>
    private string SettingsLine()
    {
        string source = _options.Source == ScanSource.Feeder
            ? UiText.Get("Scan.Feeder", "Feeder (all pages)")
            : UiText.Get("Scan.Flatbed", "Flatbed (one page)");
        string color = _options.ColorMode switch
        {
            ScanColorMode.Grayscale => UiText.Get("Scan.Gray", "Grey"),
            ScanColorMode.BlackAndWhite => UiText.Get("Scan.BlackWhite", "Black & white"),
            _ => UiText.Get("Scan.Colour", "Colour"),
        };
        string format = _format == ScanFileFormat.Jpeg ? "JPEG" : "PNG";
        return $"D {source}  ·  +/- {_options.Dpi} dpi  ·  C {color}  ·  F {format}";
    }

    /// <summary>The folder <c>W</c> writes to. A long path keeps its end, where the folder name is.</summary>
    private string SaveToLine(int width)
    {
        string folder = _folder ?? UiText.Get("Scan.NoLocalFolder", "not a local folder");
        string label = UiText.Get("Scan.SaveTo", "Save to") + ": ";
        return label + FromEnd(folder, width - label.Length);
    }

    /// <summary>
    /// The names the pages are, or were, written under: the first and the last when there are several. After a save
    /// they are labelled "Saved" and cover only the pages that reached the disk.
    /// </summary>
    private string FileLine()
    {
        if (_pages.Count == 0 || _fileStem.Length == 0)
        {
            return UiText.Get("Scan.NoFileYet", "File: none yet");
        }

        string extension = _format == ScanFileFormat.Jpeg ? ".jpg" : ".png";
        int count = _savedCount > 0 ? _savedCount : _pages.Count;
        string label = _savedCount > 0
            ? UiText.Get("Scan.SavedLabel", "Saved:")
            : UiText.Get("Scan.FileLabel", "File:");
        string first = FileName(_fileStem, 0, _pages.Count, extension);
        string names = count == 1
            ? first
            : $"{first} … {FileName(_fileStem, count - 1, _pages.Count, extension)}";
        return $"{label} {names}";
    }

    private string PageCaption()
    {
        if (_pages.Count == 0)
        {
            return UiText.Get("Scan.NoPages", "No page yet");
        }

        string caption = string.Format(UiText.Get("Scan.PageOf", "Page {0} of {1}"), _pageIndex + 1, _pages.Count);
        SKBitmap? bitmap = _pageIndex < _bitmaps.Count ? _bitmaps[_pageIndex] : null;
        if (bitmap is null)
        {
            return caption;
        }

        return $"{caption}  ·  {string.Format(UiText.Get("Scan.PixelSize", "{0} × {1} px"), bitmap.Width, bitmap.Height)}";
    }

    /// <summary>The last <paramref name="width"/> characters of <paramref name="text"/>, with an ellipsis in front when it is cut.</summary>
    private static string FromEnd(string text, int width)
    {
        if (text.Length <= width)
        {
            return text;
        }

        return "…" + text[^(width - 1)..];
    }

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        string clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    #endregion

    #region Test seams

    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal ScanOptions OptionsForTest { get { lock (_sync) return _options; } }
    internal int PageCountForTest { get { lock (_sync) return _pages.Count; } }
    internal int ScannerCountForTest { get { lock (_sync) return _scanners.Count; } }
    internal bool IsScanningForTest { get { lock (_sync) return _scanning; } }
    internal string FileStemForTest { get { lock (_sync) return _fileStem; } }

    #endregion
}
