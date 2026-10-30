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

    internal const string DismissHint = "Enter or Esc to continue";

    private readonly IReadOnlyList<string> _warnings;

    public StartupWarningDialog(IReadOnlyList<string> warnings, IThemeService themeService)
        : base("Warning", themeService)
    {
        _warnings = warnings;
        Title = iConsole.Localization.UiText.Get("Dialog.Warning", "Warning");
    }

    /// <summary>The warnings as display lines for an inner width; each warning starts on a new line.</summary>
    internal List<string> WrapWarnings(int innerWidth)
    {
        List<string> lines = [];
        int textWidth = Math.Max(1, innerWidth - 2);
        foreach (string warning in _warnings)
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
