using iConsole.Buffers;
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
/// the resolution; <c>C</c> the colour mode; <c>F</c> the file format saved (PNG or JPEG).</item>
/// <item><b>Scan.</b> Enter or <c>S</c> scans in the background. Esc while a scan runs cancels it first.</item>
/// <item><b>Preview.</b> The page is drawn as characters: <c>[</c> and <c>]</c> step through the pages of a feeder run.
/// The picture is decoded once, on a background task, when the scan lands.</item>
/// <item><b>Save.</b> <c>W</c> writes every page to the folder the dialog was opened on, named by the time of the save
/// (<c>scan-yyyyMMdd-HHmmss.png</c>, with <c>-001</c>… added when there is more than one page).</item>
/// </list>
/// </summary>
public class ScannerDialog : Dialog
{
    private const int DialogWidth = 84;
    private const int DialogHeight = 30;
    private const int ListRows = 4;

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
    private int _generation;
    private List<ScanPage> _pages = [];
    private List<SKBitmap?> _bitmaps = [];
    private int _pageIndex;

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
            if (_scanning || _scannersLoading)
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
            });
        }
    }

    private void AcceptResult(int generation, ScanResult result)
    {
        List<SKBitmap?> bitmaps;
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
            SetStatus(result.Message, !result.Success);
            bitmaps = _pages.Select(_ => (SKBitmap?)null).ToList();
            _bitmaps = bitmaps;
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
        lock (_sync)
        {
            folder = _folder;
            pages = [.. _pages];
            format = _format;
            if (_scanning)
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
        }

        string extension = format == ScanFileFormat.Jpeg ? ".jpg" : ".png";
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        IScanService service = _scanService;
        _pending = Task.Run(async () =>
        {
            string? lastMessage = null;
            bool failed = false;
            for (int i = 0; i < pages.Count; i++)
            {
                string suffix = pages.Count == 1 ? string.Empty : $"-{i + 1:000}";
                string path = Path.Combine(folder, $"scan-{stamp}{suffix}{extension}");
                SaveResult saved = await service.SaveAsync(pages[i], path, format);
                lastMessage = saved.Message;
                if (!saved.Success)
                {
                    failed = true;
                    break;
                }
            }

            lock (_sync)
            {
                SetStatus(lastMessage ?? string.Empty, failed);
            }

            StateHasChanged();
        });
    }

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

            // Scanners: the list, the chosen one marked.
            int listRows = Math.Min(ListRows, Math.Max(1, _scanners.Count));
            if (_scanners.Count == 0)
            {
                string text = _scannersLoading
                    ? UiText.Get("Scan.Searching", "Looking for scanners…")
                    : _listFailure ?? UiText.Get("Scan.NoScanners", "No scanner found.");
                Put(buffer, 1, 1, iw - 2, text, colors.Foreground, colors.Background);
            }
            else
            {
                int first = Math.Max(0, Math.Min(_scannerIndex - listRows + 1, _scanners.Count - listRows));
                for (int i = 0; i < listRows; i++)
                {
                    int index = first + i;
                    ScannerDevice device = _scanners[index];
                    string marker = index == _scannerIndex ? "> " : "  ";
                    Put(buffer, 1, 1 + i, iw - 2, marker + device.Name, index == _scannerIndex ? Color.Cyan : colors.Foreground, colors.Background);
                }
            }

            int row1 = 1 + ListRows + 1;
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
            Put(buffer, 1, row1, iw - 2, $"{source}  ·  {_options.Dpi} dpi  ·  {color}  ·  {format}", colors.Foreground, colors.Background);

            string pages = _pages.Count == 0
                ? UiText.Get("Scan.NoPages", "No page yet")
                : string.Format(UiText.Get("Scan.PageOf", "Page {0} of {1}"), _pageIndex + 1, _pages.Count);
            string folder = _folder is null
                ? UiText.Get("Scan.NoLocalFolder", "not a local folder")
                : _folder;
            Put(buffer, 1, row1 + 1, iw - 2, $"{pages}  ·  {UiText.Get("Scan.SaveTo", "Save to")}: {folder}", colors.Foreground, colors.Background);

            // Preview: the current page as characters, in the rows that remain above the status line.
            int previewTop = row1 + 3;
            int previewRows = ih - previewTop - 2;
            int previewColumns = iw - 2;
            if (previewRows > 0 && previewColumns > 0)
            {
                SKBitmap? bitmap = _pageIndex < _bitmaps.Count ? _bitmaps[_pageIndex] : null;
                if (bitmap is not null)
                {
                    IReadOnlyList<string> lines = ScanImageCodec.AsciiPreview(bitmap, previewColumns, previewRows);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Put(buffer, 1, previewTop + i, previewColumns, lines[i], colors.Foreground, colors.Background);
                    }
                }
                else
                {
                    string hint = _scanning
                        ? UiText.Get("Scan.Scanning", "Scanning…")
                        : UiText.Get("Scan.Empty", "Choose a scanner and press Enter to scan.");
                    Put(buffer, 1, previewTop, previewColumns, hint, colors.Foreground, colors.Background);
                }
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colors.Error : Color.Cyan, colors.Background);
            string hints = string.Join("  ", GetKeyHints().Select(h => $"{h.Key}:{h.Description}"));
            Put(buffer, 1, ih - 1, iw - 2, hints.FitHints(iw - 2), colors.Foreground, colors.Background);
        }
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

    #endregion
}
