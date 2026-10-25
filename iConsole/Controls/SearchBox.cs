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

    /// <summary>
    /// Fired when the user presses Enter in the box, with the current text. Enter is only consumed
    /// while someone listens, so a dialog's default button still gets it otherwise.
    /// </summary>
    public event EventHandler<string>? Submitted;

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

    // Set while a key is being handled: the key reports its own change once it has finished, so Clear (run by Escape) stays quiet.
    private bool _isHandlingKey;

    /// <summary>
    /// Resets the query to empty, fires <see cref="SearchChanged"/> if that changed it, and then fires <see cref="Cleared"/>.
    /// A caller outside a key press, such as a host that empties the box itself, gets the change reported here.
    /// </summary>
    public void Clear()
    {
        var before = Text;
        Text = string.Empty;
        ScrollableModel.CursorPosition.X = 0;
        ScrollableModel.ScrollPosition.X = 0;
        if (!_isHandlingKey && Text != before)
        {
            SearchChanged?.Invoke(this, Text);
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The text keys of <see cref="TextBox"/>, plus Escape, which clears the query.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }

        if (IsEnabled)
        {
            yield return ("Esc", "Clear the search");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var before = Text;
        bool handled;
        _isHandlingKey = true;
        try
        {
            handled = await base.HandleKeyEventAsync(keyEvent);
        }
        finally
        {
            _isHandlingKey = false;
        }

        if (!handled && IsFocused && IsEnabled && keyEvent.ConsoleKey == ConsoleKey.Enter && Submitted is not null)
        {
            Submitted.Invoke(this, Text);
            handled = true;
        }
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
