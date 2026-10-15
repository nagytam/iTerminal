using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A <see cref="TextBox"/> variant with a styled search prefix (<c>/ </c>) and
/// dedicated <see cref="SearchChanged"/> / <see cref="Cleared"/> events.
/// All editing behaviour (cursor, backspace, Home/End, scroll) is inherited from
/// <see cref="TextBox"/>; only the render path and Escape handling are overridden.
/// </summary>
public class SearchBox : TextBox
{
    /// <summary>Character rendered before the input field. Default is <c>/</c>.</summary>
    public string Prefix { get; set; } = "/";

    /// <summary>Fired whenever the search text changes, with the current text as the argument.</summary>
    public event EventHandler<string>? SearchChanged;

    /// <summary>Fired when the user presses Escape, after the text has been cleared.</summary>
    public event EventHandler? Cleared;

    public SearchBox(
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(beepService, dateTimeProviderService, themeService)
    {
        AddCommand(new ClearSearchCommand());
    }

    /// <summary>Resets the query to empty and fires <see cref="Cleared"/>.</summary>
    public void Clear()
    {
        Text = string.Empty;
        ScrollableModel.CursorPosition.X = 0;
        ScrollableModel.ScrollPosition.X = 0;
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var before = Text;
        var handled = await base.HandleKeyEventAsync(keyEvent);
        if (Text != before)
        {
            SearchChanged?.Invoke(this, Text);
        }
        return handled;
    }

    /// <summary>The <see cref="Prefix"/> and a space; no colon, unlike a labelled text box.</summary>
    public override string LabelPrefix => Prefix + " ";

    /// <summary>The prefix is drawn in the accent colour whatever the focus, so it reads as a search glyph.</summary>
    protected override ConxelString CreateLabel(ColorScheme colorScheme, Color foreground, Color background) =>
        new(LabelPrefix, colorScheme.SelectedFocusedForeground, background);
}
