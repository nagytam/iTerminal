using System.Text;

namespace iConsole.Controls;

/// <summary>
/// One entry of a <see cref="ContextMenu"/>: a title, an optional key hint, and what it does when chosen. An entry can also
/// carry a check mark (<see cref="IsChecked"/>), a hot key (an <c>&amp;</c> in the title) or a submenu (<see cref="Items"/>).
/// </summary>
public class ContextMenuItem
{
    public ContextMenuItem(string title)
    {
        Title = title ?? string.Empty;
    }

    /// <summary>A line between two groups of entries; it can not be chosen.</summary>
    public static ContextMenuItem Separator() => new(string.Empty) { IsSeparator = true, IsEnabled = false };

    /// <summary>
    /// The title as given. An <c>&amp;</c> before a letter or digit makes that character the entry's hot key (<c>"&amp;Open"</c>
    /// is drawn <c>Open</c> with the O underlined, and pressing O chooses it); <c>&amp;&amp;</c> is a literal ampersand, and
    /// an <c>&amp;</c> before anything else is left as it is.
    /// </summary>
    public string Title { get; set; }

    /// <summary>The title as drawn: without the <c>&amp;</c> that marks the hot key.</summary>
    public string Text => Parse(Title).Text;

    /// <summary>The hot key, or null when the title marks none.</summary>
    public char? HotKey
    {
        get
        {
            (string text, int index) = Parse(Title);
            return index >= 0 ? text[index] : null;
        }
    }

    /// <summary>Where the hot key is in <see cref="Text"/>, or -1.</summary>
    public int HotKeyIndex => Parse(Title).HotKeyIndex;

    /// <summary>The key that does the same thing elsewhere ("F5", "Ctrl+C"), shown at the right edge. Only a hint.</summary>
    public string Shortcut { get; set; } = string.Empty;

    /// <summary>Run after the menu has closed and focus has gone back to where it was.</summary>
    public Func<Task>? Execute { get; set; }

    /// <summary>A disabled entry is drawn faint and is skipped by the keyboard.</summary>
    public bool IsEnabled { get; set; } = true;

    public bool IsSeparator { get; init; }

    /// <summary>
    /// Null for an entry that is not a switch. True or false makes it one: it is drawn with a check mark when true, and
    /// choosing it turns it over before <see cref="Execute"/> runs, which can read the new value.
    /// </summary>
    public bool? IsChecked { get; set; }

    /// <summary>The entries of the submenu this one opens. A choice with a submenu opens it instead of running <see cref="Execute"/>.</summary>
    public IList<ContextMenuItem> Items { get; } = [];

    public bool HasSubmenu => Items.Count > 0;

    /// <summary>Whether the keyboard can stop on this entry.</summary>
    public bool CanSelect => IsEnabled && !IsSeparator;

    private static (string Text, int HotKeyIndex) Parse(string title)
    {
        if (title.IndexOf('&') < 0)
        {
            return (title, -1);
        }

        StringBuilder text = new(title.Length);
        int hotKeyIndex = -1;
        for (int i = 0; i < title.Length; i++)
        {
            char c = title[i];
            if (c == '&' && i + 1 < title.Length)
            {
                char next = title[i + 1];
                if (next == '&')
                {
                    text.Append('&');
                    i++;
                    continue;
                }

                if (char.IsLetterOrDigit(next))
                {
                    // Only the first marker is a hot key; a later one is dropped and its character kept.
                    if (hotKeyIndex < 0)
                    {
                        hotKeyIndex = text.Length;
                    }

                    continue;
                }
            }

            text.Append(c);
        }

        return (text.ToString(), hotKeyIndex);
    }
}
