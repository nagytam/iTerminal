using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.Properties;
using iTerminal.Core.Services.Wipe;

namespace iTerminal.Core.Dialogs;

/// <summary>Where <see cref="SecureWipeDialog"/> is: choosing, waiting for the second Enter, overwriting, or over.</summary>
public enum WipePhase
{
    Choosing,
    Armed,
    Running,
    Finished,
}

/// <summary>
/// Ctrl+Shift+F8: overwrites the marked files and folders (or the one under the cursor) and deletes
/// them, so they cannot be read back out of the free space.
///
/// <list type="bullet">
/// <item><b>It cannot be undone, so Enter asks twice.</b> The first Enter arms it and says what is
/// about to be destroyed; the second runs it; any other key withdraws. Same as the sync in the
/// compare dialog, and for the same reason: there is no recycle bin to take it back from.</item>
/// <item><b>The wipe runs behind the dialog</b> (<see cref="WipeTask"/>) with the byte count shown;
/// closing the dialog stops it — files already wiped stay wiped, the rest are untouched.</item>
/// <item><b>Local disk only</b>: an overwrite is a promise about the sectors under one file system,
/// which a remote or archive drive cannot keep.</item>
/// <item><b>The dialog says what an overwrite cannot do</b> — SSDs, snapshots and copy-on-write file
/// systems keep copies — because a "secure" button that doesn't say so is worse than none.</item>
/// <item>A link in the selection is removed, not followed.</item>
/// </list>
/// </summary>
public class SecureWipeDialog : Dialog
{
    private readonly ISecureWipeService _wipeService;
    private readonly INotificationService _notifications;
    private readonly IContentModelService _contentModelService;
    private readonly InfoLines _info;
    private CancellationTokenSource? _cancellation;
    private long _bytesDone;
    private long _bytesTotal;
    private string _current = string.Empty;

    /// <summary>The first line, built once in <see cref="Load"/> rather than on every idle pass.</summary>
    private string _heading = string.Empty;

    /// <summary>What a wipe writes over every pass, counted off the UI thread; -1 while it is still counting.</summary>
    private long _totalWrite = -1;

    /// <summary>When the dialog was armed (<see cref="Environment.TickCount64"/>), for the Enter guard.</summary>
    private long _armedAt;

    /// <summary>How long after arming an Enter is ignored. A held or doubled Enter must not arm and confirm in one go.</summary>
    public TimeSpan ArmGuard { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>The count started by <see cref="Arm"/>; nothing waits on it, it exists so a test can.</summary>
    public Task CountTask { get; private set; } = Task.CompletedTask;

    /// <summary>Milliseconds for the disk graphic's animation; replaceable so a test can fix a frame.</summary>
    internal Func<long> DiskTickSource { get; set; } = () => Environment.TickCount64;

    public SecureWipeDialog(
        ISecureWipeService wipeService,
        INotificationService notifications,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base(nameof(SecureWipeDialog), themeService)
    {
        _wipeService = wipeService;
        _notifications = notifications;
        _contentModelService = contentModelService;
        Title = iConsole.Localization.UiText.Get("Dialog.SecureWipe", "Secure wipe");
        _info = new InfoLines(themeService);
        AddControl(_info);
    }

    /// <summary>What will be wiped.</summary>
    public IReadOnlyList<string> Paths { get; private set; } = [];

    public WipePattern Pattern { get; private set; } = WipePattern.Random;

    public WipePhase Phase { get; private set; } = WipePhase.Choosing;

    /// <summary>The outcome, once <see cref="Phase"/> is <see cref="WipePhase.Finished"/>.</summary>
    public WipeResult? Result { get; private set; }

    /// <summary>The wipe started by the second Enter; nothing waits on it, it exists so a test can.</summary>
    public Task WipeTask { get; private set; } = Task.CompletedTask;

    /// <summary>The lines as drawn.</summary>
    public string Details => _info.Text;

    /// <summary>Sets what to wipe; call before showing the dialog.</summary>
    public void Load(IEnumerable<string> paths)
    {
        Paths = [.. paths];
        Pattern = WipePattern.Random;
        Phase = WipePhase.Choosing;
        Result = null;
        Interlocked.Exchange(ref _bytesDone, 0);
        Interlocked.Exchange(ref _bytesTotal, 0);
        Interlocked.Exchange(ref _totalWrite, -1);
        _current = string.Empty;
        _heading = BuildHeading(Paths);
    }

    /// <summary>The names of up to ten items; the rest are counted, so a folder of twenty thousand rows costs nothing here.</summary>
    private static string BuildHeading(IReadOnlyList<string> paths)
    {
        const int MaxNames = 10;
        var names = paths.Take(MaxNames)
            .Select(path => Path.GetFileName(path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : path)
            .ToList();
        if (paths.Count == 1)
        {
            return string.Format(iConsole.Localization.UiText.Get("Wipe.Name", "Wipe  {0}"), names[0]);
        }

        var list = string.Join(", ", names) + (paths.Count > names.Count ? ", …" : string.Empty);
        return string.Format(iConsole.Localization.UiText.Get("Wipe.Names", "Wipe  {0} items: {1}"), paths.Count, list);
    }

    private static readonly WipePattern[] Methods = [WipePattern.Random, WipePattern.ThreePass, WipePattern.SevenPass];

    public static string PatternName(WipePattern pattern) => pattern switch
    {
        WipePattern.ThreePass => iConsole.Localization.UiText.Get("Wipe.Pass3", "3 passes (zeros, ones, random)"),
        WipePattern.SevenPass => iConsole.Localization.UiText.Get("Wipe.Pass7", "7 passes"),
        _ => iConsole.Localization.UiText.Get("Wipe.Pass1", "1 pass of random bytes"),
    };

    public void ChoosePattern(WipePattern pattern)
    {
        if (Phase is WipePhase.Choosing or WipePhase.Armed)
        {
            Pattern = pattern;
            Phase = WipePhase.Choosing;
            UpdateLines();
        }
    }

    /// <summary>The first Enter: says what is about to happen and waits for the second.</summary>
    public void Arm()
    {
        if (Phase == WipePhase.Choosing && Paths.Count > 0)
        {
            Phase = WipePhase.Armed;
            _armedAt = Environment.TickCount64;
            Interlocked.Exchange(ref _totalWrite, -1);
            CountTask = CountWrite(Paths, Pattern);
            UpdateLines();
        }
    }

    /// <summary>Walks the tree for the armed screen's total, on a background task; the result is picked up on the next idle pass.</summary>
    private Task CountWrite(IReadOnlyList<string> paths, WipePattern pattern) => Task.Run(() =>
    {
        long total;
        try
        {
            total = _wipeService.Measure(paths, pattern);
        }
        catch (Exception)
        {
            // Shown as nothing rather than a wrong total; the wipe reports its own failures.
            total = 0;
        }

        Interlocked.Exchange(ref _totalWrite, total);
    });

    /// <summary>The second Enter: starts the wipe.</summary>
    public void Confirm()
    {
        if (Phase != WipePhase.Armed)
        {
            return;
        }

        Phase = WipePhase.Running;
        _cancellation?.Dispose();
        var cancellation = _cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        var paths = Paths;
        var pattern = Pattern;
        var progress = new InlineProgress(p =>
        {
            Interlocked.Exchange(ref _bytesDone, p.BytesDone);
            Interlocked.Exchange(ref _bytesTotal, p.BytesTotal);
            _current = p.CurrentFile;
        });
        WipeTask = Task.Run(async () =>
        {
            WipeResult result;
            try
            {
                result = await _wipeService.WipeAsync(paths, pattern, progress, token);
            }
            catch (Exception exception)
            {
                result = new WipeResult(0, 0, 0, [exception.Message], Cancelled: false);
            }

            Result = result;
            Phase = WipePhase.Finished;
            RefreshPanels();
            Announce(result);
        });
        UpdateLines();
    }

    private void Announce(WipeResult result)
    {
        var what = string.Format(UiText.Get("Wipe.What", "{0} file(s), {1}"), result.Files, FilePropertiesService.FormatSize(result.Bytes));
        if (result.Cancelled)
        {
            _notifications.Notify(UiText.Get("Notify.WipeStopped", "Secure wipe stopped"), string.Format(UiText.Get("Notify.WipeStoppedBody", "{0} wiped before it was cancelled."), what), NotificationSeverity.Warning);
        }
        else if (result.Failures.Count > 0)
        {
            _notifications.Notify(UiText.Get("Notify.WipeErrors", "Secure wipe finished with errors"), string.Format(UiText.Get("Notify.WipeErrorsBody", "{0} wiped; {1} failed. First: {2}"), what, result.Failures.Count, result.Failures[0]), NotificationSeverity.Error);
        }
        else
        {
            _notifications.Notify(UiText.Get("Notify.WipeFinished", "Secure wipe finished"), string.Format(UiText.Get("Notify.WipeFinishedBody", "{0} overwritten and deleted."), what), NotificationSeverity.Success);
        }
    }

    private void RefreshPanels()
    {
        foreach (var model in _contentModelService?.ContentModels.ToList() ?? [])
        {
            try
            {
                model.Refresh(forceRefresh: true);
            }
            catch (Exception)
            {
                // A panel that cannot be re-read is not the wipe's failure.
            }
        }
    }

    private sealed class InlineProgress(Action<WipeProgress> report) : IProgress<WipeProgress>
    {
        public void Report(WipeProgress value) => report(value);
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 4, 50, 78), 16));
        }

        _info.Position = new Position(2, 1);
        _info.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Math.Max(1, ViewSize.Height - 3));
        base.OnInitializeLayout();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        UpdateLines();
    }

    public override void OnIdle()
    {
        base.OnIdle();

        // Rebuild while a run moves its progress or the armed count lands, and once when the run ends on its worker thread.
        if (Phase is WipePhase.Running or WipePhase.Armed || Phase != _shownPhase)
        {
            UpdateLines();
        }
    }

    /// <summary>The phase the lines were last built for; <see cref="OnIdle"/> rebuilds when it changes.</summary>
    private WipePhase _shownPhase = WipePhase.Choosing;

    /// <summary>
    /// Serializes <see cref="UpdateLines"/>: it runs on the key thread and on the idle thread. Without it the idle thread could build the
    /// armed lines, lose to a key that withdrew the arming, and then store its stale lines over the new ones for good.
    /// </summary>
    private readonly object _linesLock = new();

    private void UpdateLines()
    {
        lock (_linesLock)
        {
            BuildLines();
        }
    }

    private void BuildLines()
    {
        // Read once, inside the lock: the last writer then always shows the latest phase.
        WipePhase phase = Phase;
        _shownPhase = phase;
        var colors = ThemeService.CurrentColorScheme;
        var width = Math.Max(16, (int)_info.ViewSize.Width);
        var lines = new List<InfoLines.Line>();
        lines.Add(new(_heading.Shrink(width) ?? string.Empty));

        switch (phase)
        {
            case WipePhase.Choosing:
                lines.Add(default);
                lines.Add(new(string.Format(iConsole.Localization.UiText.Get("Wipe.Method", "Method  {0}"), PatternName(Pattern))));
                lines.Add(new(iConsole.Localization.UiText.Get("Wipe.MethodKeys", "        1: one pass   3: three   7: seven   (or ↑↓)"), colors.DisabledForeground));
                lines.Add(default);
                // Short lines: the one-line form was 72 columns, and "undone." was cut off on 80.
                lines.Add(new(iConsole.Localization.UiText.Get("Wipe.Overwritten", "The contents are overwritten and the names removed.")));
                lines.Add(new(iConsole.Localization.UiText.Get("Wipe.NoUndo", "This cannot be undone."), colors.Error));
                lines.Add(new(iConsole.Localization.UiText.Get("Wipe.Copies1", "It cannot reach copies that snapshots, backups,"), colors.DisabledForeground));
                lines.Add(new(iConsole.Localization.UiText.Get("Wipe.Copies2", "copy-on-write file systems or SSDs keep."), colors.DisabledForeground));
                lines.Add(default);
                lines.Add(new(iConsole.Localization.UiText.Get("Wipe.Continue", "Enter:continue"), colors.DisabledForeground));
                break;
            case WipePhase.Armed:
                {
                    lines.Add(default);
                    lines.Add(new(string.Format(iConsole.Localization.UiText.Get("Wipe.Method", "Method  {0}"), PatternName(Pattern))));
                    lines.Add(default);
                    lines.Add(new(iConsole.Localization.UiText.Get("Wipe.Armed1", "This destroys the data for good. Press Enter again to wipe,"), colors.Error));
                    lines.Add(new(iConsole.Localization.UiText.Get("Wipe.Armed2", "any other key to go back."), colors.Error));
                    lines.Add(default);
                    var total = Interlocked.Read(ref _totalWrite);
                    lines.Add(total < 0
                        ? new(iConsole.Localization.UiText.Get("Wipe.Counting", "Counting the files…"), colors.DisabledForeground)
                        : new(string.Format(iConsole.Localization.UiText.Get("Wipe.Total", "{0} written in total, over every pass."), FilePropertiesService.FormatSize(total)), colors.DisabledForeground));
                    break;
                }
            case WipePhase.Running:
                {
                    var done = Interlocked.Read(ref _bytesDone);
                    var total = Interlocked.Read(ref _bytesTotal);
                    var percent = total > 0 ? $" {Math.Min(100, done * 100 / total)}%" : string.Empty;
                    lines.Add(default);
                    lines.Add(new(string.Format(iConsole.Localization.UiText.Get("Wipe.Overwriting", "Overwriting…{0}  {1} of {2}"), percent, FilePropertiesService.FormatSize(done), FilePropertiesService.FormatSize(total))));
                    var fraction = total > 0 ? (double)done / total : 0;
                    lines.Add(new(WipeDiskGraphic.Render(fraction, Math.Clamp(width - 12, 8, 60), DiskTickSource()), colors.Error));
                    lines.Add(new(Path.GetFileName(_current).Shrink(width) ?? string.Empty, colors.DisabledForeground));
                    lines.Add(default);
                    lines.Add(new(iConsole.Localization.UiText.Get("Wipe.EscStops", "Esc stops it; files already wiped stay wiped."), colors.DisabledForeground));
                    break;
                }
            default:
                {
                    lines.Add(default);
                    var result = Result;
                    if (result is null)
                    {
                        break;
                    }

                    lines.Add(new(result.Cancelled
                        ? string.Format(UiText.Get("Wipe.Stopped", "Stopped: {0} file(s) wiped."), result.Files)
                        : string.Format(UiText.Get("Wipe.Done", "Done: {0} file(s), {1} folder(s), {2} overwritten."), result.Files, result.Folders, FilePropertiesService.FormatSize(result.Bytes)),
                        result.Success ? Color.Green : colors.Error));
                    var failedLabel = UiText.Get("Wipe.Failed", "Failed: {0}");
                    foreach (var failure in result.Failures.Take(Math.Max(0, (int)_info.ViewSize.Height - 4)))
                    {
                        lines.Add(new((string.Format(failedLabel, failure)).Shrink(width) ?? string.Empty, colors.Error));
                    }

                    break;
                }
        }

        _info.Lines = lines;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        switch (Phase)
        {
            case WipePhase.Choosing:
                yield return ("1/3/7", UiText.Get("DialogHint.ChoosePasses", "Choose one random pass, three passes or seven passes"));
                yield return ("Up/Down", UiText.Get("DialogHint.ChoosePreviousNextMethod", "Choose the previous or next method"));
                yield return ("Enter", UiText.Get("DialogHint.ArmWipe", "Arm the wipe (nothing is erased yet)"));
                break;
            case WipePhase.Armed:
                yield return ("Enter", UiText.Get("DialogHint.ConfirmFilesErased", "Confirm: the files are erased for good"));
                yield return ("Any other key", UiText.Get("DialogHint.WithdrawChooseAgain", "Withdraw and choose again"));
                break;
            case WipePhase.Finished:
                yield return ("Enter", iConsole.Localization.UiText.Get("Common.Close", "Close"));
                break;
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Alt && !keyEvent.Control;
        if (Phase == WipePhase.Armed)
        {
            // Esc is the dialog's ahead of anything; every other key but Enter withdraws.
            if (keyEvent.ConsoleKey == ConsoleKey.Enter && plain)
            {
                // An Enter that lands right after arming is a held or doubled key, not a second decision: ignore it.
                if (Environment.TickCount64 - _armedAt >= ArmGuard.TotalMilliseconds)
                {
                    Confirm();
                }

                return true;
            }

            if (keyEvent.ConsoleKey != ConsoleKey.Escape)
            {
                Phase = WipePhase.Choosing;
                UpdateLines();
                return true;
            }
        }
        else if (Phase == WipePhase.Choosing && plain)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.D1 or ConsoleKey.NumPad1:
                    ChoosePattern(WipePattern.Random);
                    return true;
                case ConsoleKey.D3 or ConsoleKey.NumPad3:
                    ChoosePattern(WipePattern.ThreePass);
                    return true;
                case ConsoleKey.D7 or ConsoleKey.NumPad7:
                    ChoosePattern(WipePattern.SevenPass);
                    return true;
                case ConsoleKey.UpArrow or ConsoleKey.LeftArrow:
                    ChoosePattern(Methods[Math.Max(0, Array.IndexOf(Methods, Pattern) - 1)]);
                    return true;
                case ConsoleKey.DownArrow or ConsoleKey.RightArrow:
                    ChoosePattern(Methods[Math.Min(Methods.Length - 1, Array.IndexOf(Methods, Pattern) + 1)]);
                    return true;
                case ConsoleKey.Enter:
                    Arm();
                    return true;
            }
        }
        else if (Phase == WipePhase.Finished && plain && keyEvent.ConsoleKey == ConsoleKey.Enter)
        {
            Close();
            return true;
        }

        var handled = await base.HandleKeyEventAsync(keyEvent);
        UpdateLines();
        return handled;
    }

    /// <summary>A click on the method (or the line of choices under it) steps to the next method, round to the first.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (Phase == WipePhase.Choosing && mouseEvent.LeftClicked && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            long row = mouseEvent.Position.Y - _info.AbsolutePosition.Y;
            long column = mouseEvent.Position.X - _info.AbsolutePosition.X;
            if (row is 2 or 3 && column >= 0 && column < _info.ViewSize.Width)
            {
                ChoosePattern(Methods[(Array.IndexOf(Methods, Pattern) + 1) % Methods.Length]);
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override void Close()
    {
        // Stops after the file under way; what was wiped stays wiped.
        _cancellation?.Cancel();
        base.Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        // The footer names only the keys that work in the current phase.
        var footer = Phase switch
        {
            WipePhase.Choosing => iConsole.Localization.UiText.Get("Wipe.FooterChoosing", " Enter:arm  1/3/7 or ↑↓:passes  Esc:close "),
            WipePhase.Armed => iConsole.Localization.UiText.Get("Wipe.FooterArmed", " Enter:wipe for good  any other key:go back "),
            WipePhase.Running => iConsole.Localization.UiText.Get("Wipe.FooterRunning", " Esc:stop (wiped files stay wiped) "),
            _ => iConsole.Localization.UiText.Get("Wipe.FooterFinished", " Enter:close "),
        };
        footer = footer.FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
