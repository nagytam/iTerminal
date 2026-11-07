using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shown at start-up when something the user should know about went wrong (a second instance holding
/// the database, a schema reset, ...). Every warning is wrapped rather than cut at the border — these
/// are sentences, and the half that used to be cut off was usually the half saying what to do — and
/// the dialog is sized to what it holds, within the screen.
/// </summary>
public class StartupWarningDialog : Dialog
{
    private const int PreferredWidth = 80;

    /// <summary>Rows that are not warning text: top border, blank row, blank row, hint row, bottom border.</summary>
    private const int ChromeRows = 5;

    internal static string DismissHint => iConsole.Localization.UiText.Get("Startup.Dismiss", "Enter or Esc to continue");

    private readonly IReadOnlyList<string> _warnings;
    private readonly Func<string?>? _stopOtherInstance;
    private volatile string _stopStatus = string.Empty;
    private int _stopStarted;

    /// <param name="stopOtherInstance">
    /// When given, K offers to stop the other instance. It returns null on success or the reason it failed,
    /// and runs on a background task: it waits for the process to exit.
    /// </param>
    public StartupWarningDialog(IReadOnlyList<string> warnings, IThemeService themeService, Func<string?>? stopOtherInstance = null)
        : base(iConsole.Localization.UiText.Get("Dialog.Warning", "Warning"), themeService)
    {
        _warnings = warnings;
        _stopOtherInstance = stopOtherInstance;
        Title = iConsole.Localization.UiText.Get("Dialog.Warning", "Warning");
        if (stopOtherInstance is not null)
        {
            _stopStatus = UiText.Get("Startup.StopOther", "Press K to stop the other instance and remove its lock file.");
        }
    }

    /// <summary>What the stop offer currently says: the prompt, then progress, then the outcome.</summary>
    internal string StopStatus => _stopStatus;

    /// <summary>Starts stopping the other instance, once. Returns the running task, or null when there is no offer or it was already taken.</summary>
    internal Task? StopOtherInstance()
    {
        if (_stopOtherInstance is null || Interlocked.Exchange(ref _stopStarted, 1) != 0)
        {
            return null;
        }

        _stopStatus = UiText.Get("Startup.StopOtherRunning", "Stopping the other instance...");
        return Task.Run(() =>
        {
            string? error = _stopOtherInstance();
            _stopStatus = error is null
                ? UiText.Get("Startup.StopOtherDone", "The other instance was stopped. Restart iTerminal to use the saved database.")
                : string.Format(UiText.Get("Startup.StopOtherFailed", "The other instance could not be stopped: {0}"), error);
        });
    }

    /// <summary>The warnings as display lines for an inner width; each warning starts on a new line.</summary>
    internal List<string> WrapWarnings(int innerWidth)
    {
        List<string> lines = [];
        int textWidth = Math.Max(1, innerWidth - 2);
        IEnumerable<string> all = _stopStatus.Length > 0 ? _warnings.Append(_stopStatus) : _warnings;
        foreach (string warning in all)
        {
            List<string> wrapped = (warning ?? string.Empty).WrapWords(textWidth);
            lines.AddRange(wrapped.Count == 0 ? [string.Empty] : wrapped);
        }

        return lines;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent is not null)
        {
            // Clamped to the screen so a narrow terminal gets a narrower dialog rather than a
            // negative position, which is what the old hand-rolled centring produced.
            int width = (int)Math.Max(20, Math.Min(PreferredWidth, (long)Parent.ViewSize.Width));
            int lineCount = WrapWarnings(width - 2).Count;
            int height = (int)Math.Max(ChromeRows + 1, Math.Min(lineCount + ChromeRows, (long)Parent.ViewSize.Height));
            CenterInParent(new Size(width, height));
        }
    }

    /// <summary>
    /// The wrapped warning rows that fit between the title and the hint. When they do not all fit,
    /// the last one says so with an ellipsis rather than stopping mid-thought.
    /// </summary>
    internal IEnumerable<(int Row, string Line)> VisibleLines(int innerWidth)
    {
        List<string> text = WrapWarnings(innerWidth);
        int visibleRows = Math.Max(0, (int)ViewSize.Height - ChromeRows);
        for (int i = 0; i < Math.Min(text.Count, visibleRows); i++)
        {
            string line = " " + text[i];
            if (i == visibleRows - 1 && text.Count > visibleRows)
            {
                line = " " + text[i].Cut(Math.Max(0, innerWidth - 3)) + "…";
            }

            yield return (i, line);
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("DialogHint.DismissWarning", "Dismiss the warning"));
        if (_stopOtherInstance is not null)
        {
            yield return ("K", UiText.Get("DialogHint.StopOtherInstance", "Stop the other instance"));
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // A notice has one thing to do with it. Enter is the key people press to "OK" a message;
        // before, only Esc worked, and nothing on screen said so.
        if (keyEvent.ConsoleKey == ConsoleKey.Enter)
        {
            Close();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.K && !keyEvent.Alt && !keyEvent.Control && StopOtherInstance() is not null)
        {
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        foreach ((int row, string line) in VisibleLines(innerWidth))
        {
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 2 + row),
                new Size(innerWidth, 1),
                new ConxelString(line.ExpandRight(innerWidth), Color.DarkYellow, colorScheme.Background));
        }

        string hint = DismissHint.Length <= innerWidth ? DismissHint.Expand(innerWidth) : DismissHint.Cut(innerWidth);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 2),
            new Size(innerWidth, 1),
            new ConxelString(hint, colorScheme.DisabledForeground, colorScheme.Background));
    }
}
