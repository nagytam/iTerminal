using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Base for full-screen, modal split-pane dialogs (a list/section column on the left, a detail
/// pane on the right) such as <see cref="Git.GitClientDialog"/> and
/// <see cref="Dotnet.SolutionViewerDialog"/>. Centralises the shared header/footer chrome — the
/// section-header bands and single-line writes — that surrounds their composed
/// <see cref="iConsole.Controls.ListBox{T}"/> content, so the dialogs render consistently and
/// stay in step with the active theme.
/// </summary>
public abstract class SplitPaneDialog : Dialog
{
    protected SplitPaneDialog(string title, IThemeService themeService)
        : base(title, themeService)
    {
    }

    /// <summary>
    /// Draws a section-header band. A focused section reuses the theme's selection highlight so the
    /// header and the selected row inside it read as one unit; an unfocused header is a quieter
    /// themed band.
    /// </summary>
    protected void DrawSectionHeader(IBuffer buffer, int x, int y, int w, string title, bool focused, ColorScheme colorScheme)
    {
        var fg = focused ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
        var bg = focused ? colorScheme.SelectedFocusedBackground : colorScheme.ButtonBackground;
        WriteLine(buffer, x, y, w, " " + title, fg, bg);
    }

    /// <summary>
    /// A footer row holding a status message and the dialog's key hints together. The status comes
    /// first, in its own colour, because it is what just happened; the hints fill what is left. Showing
    /// one <em>or</em> the other — which is what these dialogs used to do — hid the keys for the rest
    /// of the session as soon as the first command reported anything.
    /// </summary>
    protected void DrawStatusAndHints(IBuffer buffer, int x, int y, int w, string? status, Color statusForeground, string hints, ColorScheme colorScheme)
    {
        if (w <= 0)
        {
            return;
        }

        status ??= string.Empty;
        if (status.Length == 0)
        {
            WriteLine(buffer, x, y, w, hints.FitHints(w), colorScheme.Foreground, colorScheme.Background);
            return;
        }

        const string separator = "  │ ";
        if (status.Length + separator.Length >= w)
        {
            WriteLine(buffer, x, y, w, status, statusForeground, colorScheme.Background);
            return;
        }

        WriteLine(buffer, x, y, status.Length, status, statusForeground, colorScheme.Background);
        WriteLine(buffer, x + status.Length, y, w - status.Length, separator + hints.FitHints(w - status.Length - separator.Length), colorScheme.Foreground, colorScheme.Background);
    }

    /// <summary>Writes a single padded/truncated line of styled text at <paramref name="x"/>,<paramref name="y"/>.</summary>
    protected void WriteLine(IBuffer buffer, int x, int y, int w, string text, Color foreground, Color background)
    {
        if (w <= 0)
        {
            return;
        }

        text ??= string.Empty;
        text = text.Length > w ? text[..w] : text.PadRight(w);
        buffer.Write(ZOrder, new Position(x, y), new Size(w, 1), new ConxelString(text, foreground, background));
    }
}
