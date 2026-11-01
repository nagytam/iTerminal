using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Printing;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+P: print the file under the cursor, and manage the queue of the printer.
///
/// <list type="bullet">
/// <item><b>One dialog for both.</b> It opens on the queue of the chosen printer whether or not a file is under the
/// cursor; with none, the file line says so and Enter prints nothing — the dialog is then only the queue manager.</item>
/// <item><b>Left/Right choose the printer</b> (remembered in the <c>Print.Printer</c> setting), <c>+</c>/<c>-</c> the number
/// of copies, Enter prints, P holds or resumes the job under the cursor, Del asks and then cancels it (y), F5 reads
/// the queue again.</item>
/// <item><b>Nothing waits on a tool.</b> Every call runs on a background task and the status line says how it went. The
/// queue is re-read on a timer while the dialog is open — 5 s on Windows, where each read starts PowerShell, 2 s
/// elsewhere — skipping a round while a call is still out, and again right after any action. A reply for a printer or
/// a request that is no longer the current one is dropped (<see cref="_generation"/>), and closing the dialog cancels
/// what is in flight.</item>
/// <item><b>Local files only</b>: the tools are handed a path.</item>
/// </list>
/// </summary>
public class PrintDialog : Dialog
{
    /// <summary>The setting that remembers the printer last used.</summary>
    internal const string PrinterSetting = "Print.Printer";

    private const int DialogWidth = 84;
    private const int DialogHeight = 22;
    private const int ListRows = 12;
    private const int MaxCopies = 99;

    private readonly IPrintService _printService;
    private readonly ISettingsService _settingsService;
    private readonly Lock _sync = new();

    private CancellationTokenSource _cancellation = new();
    private IReadOnlyList<Printer> _printers = [];
    private IReadOnlyList<PrintJob> _jobs = [];
    private int _printerIndex;
    private int _cursor;
    private int _copies = 1;
    private bool _confirmCancel;
    private bool _printersLoaded;
    private string? _listFailure;
    private string _status = string.Empty;
    private bool _statusIsError;
    private int _generation;
    private int _inFlight;
    private long _lastPoll;

    public PrintDialog(IPrintService printService, ISettingsService settingsService, IThemeService themeService)
        : base(nameof(PrintDialog), themeService)
    {
        Title = UiText.Get("Menu.Print", "Print");
        _printService = printService;
        _settingsService = settingsService;
    }

    /// <summary>The file to print, or null when the dialog was opened for the queue alone.</summary>
    public string? FilePath { get; private set; }

    /// <summary>The last action or read; nothing waits on it, it exists so a test can.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>How long the queue is left alone between two reads.</summary>
    internal TimeSpan PollInterval { get; set; } = OperatingSystem.IsWindows() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(2);

    public void Load(string? filePath)
    {
        FilePath = filePath;
        lock (_sync)
        {
            _copies = 1;
            _status = string.Empty;
            _statusIsError = false;
        }
    }

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _confirmCancel = false;
            _cursor = 0;
            _jobs = [];
            _printersLoaded = false;
            _lastPoll = Environment.TickCount64;
            _cancellation.Cancel();
            _cancellation = new CancellationTokenSource();
        }

        StartLoadingPrinters(_cancellation.Token);
        await base.ShowAsync();
    }

    public override void Close()
    {
        lock (_sync)
        {
            _cancellation.Cancel();
        }

        base.Close();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Min(DialogWidth, Math.Max(40, (int)Parent.ViewSize.Width - 2)), DialogHeight));
        }

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        bool due;
        lock (_sync)
        {
            due = _printersLoaded && Environment.TickCount64 - _lastPoll >= PollInterval.TotalMilliseconds;
        }

        if (due)
        {
            StartRefresh(force: false);
        }
    }

    #region Reading

    private void StartLoadingPrinters(CancellationToken token)
    {
        Interlocked.Increment(ref _inFlight);
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                var listing = await _printService.ListPrintersAsync(token);
                lock (_sync)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    _printers = listing.Printers;
                    _listFailure = listing.Failure;
                    _printersLoaded = true;
                    var remembered = _settingsService.Get(PrinterSetting);
                    var index = _printers.ToList().FindIndex(p => string.Equals(p.Name, remembered, StringComparison.OrdinalIgnoreCase));
                    _printerIndex = index >= 0 ? index : Math.Max(0, _printers.ToList().FindIndex(p => p.IsDefault));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _listFailure = exception.Message;
                    _printersLoaded = true;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }

            StartRefresh(force: true);
            StateHasChanged();
        });
    }

    /// <summary>Reads the queue of the chosen printer. A round that is not forced is skipped while a call is still out.</summary>
    internal void StartRefresh(bool force)
    {
        string? printer;
        CancellationToken token;
        int generation;
        lock (_sync)
        {
            printer = CurrentPrinter?.Name;
            token = _cancellation.Token;
            if (printer is null || (!force && Volatile.Read(ref _inFlight) > 0))
            {
                return;
            }

            _lastPoll = Environment.TickCount64;
            generation = Interlocked.Increment(ref _generation);
            Interlocked.Increment(ref _inFlight);
        }

        PendingOperation = Task.Run(async () =>
        {
            try
            {
                var queue = await _printService.GetQueueAsync(printer, token);
                lock (_sync)
                {
                    // A newer read, another printer or a closed dialog has made this answer old news.
                    if (generation != Volatile.Read(ref _generation) || token.IsCancellationRequested)
                    {
                        return;
                    }

                    _jobs = queue.Jobs;
                    _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _jobs.Count - 1));
                    if (queue.Failure is not null)
                    {
                        SetStatus(queue.Failure, true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    SetStatus(exception.Message, true);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }

            StateHasChanged();
        });
    }

    #endregion

    #region Keys

    private Printer? CurrentPrinter => _printerIndex >= 0 && _printerIndex < _printers.Count ? _printers[_printerIndex] : null;

    private PrintJob? SelectedJob => _cursor >= 0 && _cursor < _jobs.Count ? _jobs[_cursor] : null;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        bool confirming;
        lock (_sync)
        {
            confirming = _confirmCancel;
        }

        if (confirming)
        {
            yield return ("Y", UiText.Get("DialogHint.CancelJobYes", "Cancel the print job"));
            yield return ("Any other key", UiText.Get("DialogHint.KeepJob", "Keep the print job"));
            yield break;
        }

        yield return ("Enter", UiText.Get("DialogHint.PrintFile", "Print the file"));
        yield return ("Left/Right", UiText.Get("DialogHint.ChoosePrinter", "Choose the printer"));
        yield return ("+ / -", UiText.Get("DialogHint.Copies", "More or fewer copies"));
        yield return ("Up/Down", UiText.Get("DialogHint.ChooseJob", "Choose a print job"));
        yield return ("P", UiText.Get("DialogHint.HoldOrResumeJob", "Hold or resume the job"));
        yield return ("Delete", UiText.Get("DialogHint.CancelJobAsks", "Cancel the job (asks first)"));
        yield return ("F5", UiText.Get("DialogHint.RefreshQueue", "Read the queue again"));
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
        // Shift is allowed: '+' and a capital P (Caps Lock) both arrive with it.
        bool plain = !keyEvent.Control && !keyEvent.Alt;
        lock (_sync)
        {
            if (_confirmCancel)
            {
                // The question is answered by y; any other key (Esc included) only withdraws it.
                _confirmCancel = false;
                if (plain && char.ToLowerInvariant(keyEvent.KeyChar) == 'y')
                {
                    CancelSelected();
                }
                else
                {
                    SetStatus(string.Empty, false);
                }

                return true;
            }

            if (keyEvent.ConsoleKey == ConsoleKey.F5 && plain)
            {
                StartRefresh(force: true);
                return true;
            }

            if (!plain)
            {
                return false;
            }

            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    PrintFile();
                    return true;
                case ConsoleKey.LeftArrow:
                    return ChoosePrinter(-1);
                case ConsoleKey.RightArrow:
                    return ChoosePrinter(1);
                case ConsoleKey.UpArrow:
                    _cursor = Math.Max(0, _cursor - 1);
                    return true;
                case ConsoleKey.DownArrow:
                    _cursor = Math.Clamp(_cursor + 1, 0, Math.Max(0, _jobs.Count - 1));
                    return true;
                case ConsoleKey.Delete:
                    return AskCancel();
            }

            switch (keyEvent.KeyChar)
            {
                case '+':
                    _copies = Math.Min(MaxCopies, _copies + 1);
                    return true;
                case '-':
                    _copies = Math.Max(1, _copies - 1);
                    return true;
                case 'p' or 'P':
                    return HoldOrResume();
            }
        }

        return false;
    }

    private bool ChoosePrinter(int step)
    {
        if (_printers.Count == 0)
        {
            return true;
        }

        _printerIndex = (_printerIndex + step + _printers.Count) % _printers.Count;
        _jobs = [];
        _cursor = 0;
        _settingsService.Set(PrinterSetting, _printers[_printerIndex].Name);
        StartRefresh(force: true);
        return true;
    }

    private bool AskCancel()
    {
        if (SelectedJob is { } job)
        {
            _confirmCancel = true;
            SetStatus(Say("Print.Status.AskCancel", "Cancel job {0} ({1})? y cancels it.", job.Id, job.Document), false);
        }

        return true;
    }

    private void PrintFile()
    {
        if (FilePath is not { } path)
        {
            SetStatus(UiText.Get("Print.Status.NoFile", "There is no file under the cursor to print."), true);
            return;
        }

        if (CurrentPrinter?.Name is not { } printer)
        {
            SetStatus(_listFailure ?? UiText.Get("Print.Status.NoPrinter", "There is no printer to print on."), true);
            return;
        }

        int copies = _copies;
        SetStatus(Say("Print.Status.Sending", "Sending {0} to {1}…", Path.GetFileName(path), printer), false);
        StartAction(token => _printService.PrintFileAsync(printer, path, copies, token));
    }

    private bool HoldOrResume()
    {
        if (SelectedJob is not { } job)
        {
            return true;
        }

        SetStatus(string.Empty, false);
        StartAction(token => job.State == PrintJobState.Held
            ? _printService.ResumeJobAsync(job.Printer, job.Id, token)
            : _printService.HoldJobAsync(job.Printer, job.Id, token));
        return true;
    }

    private void CancelSelected()
    {
        if (SelectedJob is { } job)
        {
            StartAction(token => _printService.CancelJobAsync(job.Printer, job.Id, token));
        }
    }

    /// <summary>Runs an action on a task, says how it went on the status line, and reads the queue again.</summary>
    private void StartAction(Func<CancellationToken, Task<PrintResult>> action)
    {
        var token = _cancellation.Token;
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                var result = await action(token);
                lock (_sync)
                {
                    if (!token.IsCancellationRequested)
                    {
                        SetStatus(result.Message, !result.Success);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    SetStatus(exception.Message, true);
                }
            }

            StartRefresh(force: true);
            StateHasChanged();
        });
    }

    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    #endregion

    #region Drawing

    internal static string StateLabel(PrintJobState state) => state switch
    {
        PrintJobState.Printing => UiText.Get("Print.State.Printing", "Printing"),
        PrintJobState.Held => UiText.Get("Print.State.Held", "Held"),
        PrintJobState.Error => UiText.Get("Print.State.Error", "Error"),
        _ => UiText.Get("Print.State.Queued", "Queued"),
    };

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 4)
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

            string file = FilePath is { } path ? Path.GetFileName(path) : UiText.Get("Print.NoFile", "(no file under the cursor — the queue only)");
            Put(buffer, 1, 0, iw - 2, UiText.Get("Print.File", "File").PadRight(11) + file, colors.Foreground, colors.Background);
            Put(buffer, 1, 1, iw - 2, UiText.Get("Print.Printer", "Printer").PadRight(11) + PrinterLine(), Color.Cyan, colors.Background);
            Put(buffer, 1, 2, iw - 2, UiText.Get("Print.Copies", "Copies").PadRight(11) + _copies, colors.Foreground, colors.Background);

            int nameWidth = Math.Max(8, iw - 2 - 2 - 31);
            Put(buffer, 1, 4, iw - 2, "  " + UiText.Get("Print.Col.Id", "Id").PadLeft(5) + " " + UiText.Get("Print.Col.Document", "Document").PadRight(nameWidth) + " "
                + UiText.Get("Print.Col.Owner", "Owner").PadRight(10) + " " + UiText.Get("Print.Col.State", "State").PadRight(8), colors.DisabledForeground, colors.Background);
            if (_jobs.Count == 0)
            {
                Put(buffer, 1, 5, iw - 2, _printersLoaded
                    ? UiText.Get("Print.QueueEmpty", "The queue is empty.")
                    : UiText.Get("Print.Reading", "Reading the printers…"), colors.DisabledForeground, colors.Background);
            }

            int first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
            for (int i = 0; i < ListRows; i++)
            {
                int index = first + i;
                if (index >= _jobs.Count)
                {
                    continue;
                }

                var job = _jobs[index];
                bool highlighted = index == _cursor;
                Put(buffer, 1, 5 + i, iw - 2,
                    (highlighted ? "> " : "  ") + job.Id.ToString().PadLeft(5) + " " + Clip(job.Document, nameWidth).PadRight(nameWidth) + " "
                    + Clip(job.Owner, 10).PadRight(10) + " " + StateLabel(job.State).PadRight(8),
                    highlighted ? colors.SelectedFocusedForeground : (job.State == PrintJobState.Error ? colors.Error : colors.Foreground),
                    highlighted ? colors.SelectedFocusedBackground : colors.Background);
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colors.Error : Color.Cyan, colors.Background);
            Put(buffer, 1, ih - 1, iw - 2, _confirmCancel
                ? UiText.Get("Print.Hint.Confirm", "y cancels the job  any other key keeps it")
                : UiText.Get("Print.Hint.List", "Enter print  ←→ printer  +/- copies  P hold/resume  Del cancel  F5 refresh  Esc close"),
                colors.Foreground, colors.Background);
        }
    }

    private string PrinterLine()
    {
        if (CurrentPrinter is not { } printer)
        {
            return _printersLoaded
                ? _listFailure ?? UiText.Get("Print.NoPrinters", "No printers are installed.")
                : UiText.Get("Print.Reading", "Reading the printers…");
        }

        return $"{printer.Name}{(printer.IsDefault ? " " + UiText.Get("Print.Default", "(default)") : string.Empty)}  [{_printerIndex + 1}/{_printers.Count}]  {printer.Status}";
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

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    #endregion

    #region Test seams

    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal int CopiesForTest { get { lock (_sync) return _copies; } }
    internal int JobCountForTest { get { lock (_sync) return _jobs.Count; } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }
    internal string? PrinterForTest { get { lock (_sync) return CurrentPrinter?.Name; } }

    #endregion
}
