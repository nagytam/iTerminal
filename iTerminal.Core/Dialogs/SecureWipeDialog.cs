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
        Title = "Secure wipe";
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
        _current = string.Empty;
    }

    public static string PatternName(WipePattern pattern) => pattern switch
    {
        WipePattern.ThreePass => "3 passes (zeros, ones, random)",
        WipePattern.SevenPass => "7 passes",
        _ => "1 pass of random bytes",
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
            UpdateLines();
        }
    }

    /// <summary>The second Enter: starts the wipe.</summary>
    public void Confirm()
    {
        if (Phase != WipePhase.Armed)
        {
            return;
        }

        Phase = WipePhase.Running;
        var cancellation = _cancellation = new CancellationTokenSource();
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
                result = await _wipeService.WipeAsync(paths, pattern, progress, cancellation.Token);
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
        var what = $"{result.Files} file{(result.Files == 1 ? string.Empty : "s")}, {FilePropertiesService.FormatSize(result.Bytes)}";
        if (result.Cancelled)
        {
            _notifications.Notify("Secure wipe stopped", $"{what} wiped before it was cancelled.", NotificationSeverity.Warning);
        }
        else if (result.Failures.Count > 0)
        {
            _notifications.Notify("Secure wipe finished with errors", $"{what} wiped; {result.Failures.Count} failed. First: {result.Failures[0]}", NotificationSeverity.Error);
        }
        else
        {
            _notifications.Notify("Secure wipe finished", $"{what} overwritten and deleted.", NotificationSeverity.Success);
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
        UpdateLines();
    }

    private void UpdateLines()
    {
        var colors = ThemeService.CurrentColorScheme;
        var width = Math.Max(16, (int)_info.ViewSize.Width);
        var lines = new List<InfoLines.Line>();
        var names = Paths.Select(path => Path.GetFileName(path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : path).ToList();
        lines.Add(new(names.Count == 1
            ? ("Wipe  " + names[0]).Shrink(width) ?? string.Empty
            : $"Wipe  {names.Count} items: {string.Join(", ", names)}".Shrink(width) ?? string.Empty));

        switch (Phase)
        {
            case WipePhase.Choosing:
                lines.Add(default);
                lines.Add(new($"Method  {PatternName(Pattern)}"));
                lines.Add(new("        1: one pass   3: three passes   7: seven passes", colors.DisabledForeground));
                lines.Add(default);
                lines.Add(new("The contents are overwritten and the names removed. This cannot be undone."));
                lines.Add(new("It cannot reach copies that snapshots, backups,", colors.DisabledForeground));
                lines.Add(new("copy-on-write file systems or SSDs keep.", colors.DisabledForeground));
                lines.Add(default);
                lines.Add(new("Enter: continue", colors.DisabledForeground));
                break;
            case WipePhase.Armed:
                lines.Add(default);
                lines.Add(new($"Method  {PatternName(Pattern)}"));
                lines.Add(default);
                lines.Add(new("This destroys the data for good. Press Enter again to wipe,", colors.Error));
                lines.Add(new("any other key to go back.", colors.Error));
                break;
            case WipePhase.Running:
                {
                    var done = Interlocked.Read(ref _bytesDone);
                    var total = Interlocked.Read(ref _bytesTotal);
                    var percent = total > 0 ? $" {Math.Min(100, done * 100 / total)}%" : string.Empty;
                    lines.Add(default);
                    lines.Add(new($"Overwriting…{percent}  {FilePropertiesService.FormatSize(done)} of {FilePropertiesService.FormatSize(total)}"));
                    lines.Add(new(Path.GetFileName(_current).Shrink(width) ?? string.Empty, colors.DisabledForeground));
                    lines.Add(default);
                    lines.Add(new("Esc stops it; files already wiped stay wiped.", colors.DisabledForeground));
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
                        ? $"Stopped: {result.Files} file(s) wiped."
                        : $"Done: {result.Files} file(s), {result.Folders} folder(s), {FilePropertiesService.FormatSize(result.Bytes)} overwritten.",
                        result.Success ? Color.Green : colors.Error));
                    foreach (var failure in result.Failures.Take(Math.Max(0, (int)_info.ViewSize.Height - 4)))
                    {
                        lines.Add(new(("Failed: " + failure).Shrink(width) ?? string.Empty, colors.Error));
                    }

                    break;
                }
        }

        _info.Lines = lines;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Alt && !keyEvent.Control;
        if (Phase == WipePhase.Armed)
        {
            // Esc is the dialog's ahead of anything; every other key but Enter withdraws.
            if (keyEvent.ConsoleKey == ConsoleKey.Enter && plain)
            {
                Confirm();
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
                case ConsoleKey.Enter:
                    Arm();
                    return true;
            }
        }

        var handled = await base.HandleKeyEventAsync(keyEvent);
        UpdateLines();
        return handled;
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
        var footer = " Enter:wipe  1/3/7:passes  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
