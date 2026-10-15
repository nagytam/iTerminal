using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Displays a keyboard shortcut as a styled key-cap followed by an optional description,
/// e.g. <c>[F5]</c> or <c>[Ctrl+S] Save</c>.
/// Not focusable; intended for help bars and annotated menu items.
/// </summary>
public class KeybindingLabel(IThemeService themeService) : Control(themeService)
{
    /// <summary>The key binding to display. When <c>null</c> the control renders nothing.</summary>
    public Key? Binding { get; set; }

    /// <summary>Optional description appended after the key cap, separated by a space.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Foreground colour for the <c>[key]</c> cap. Defaults to the theme's focused foreground.</summary>
    public Color? KeyForeground { get; set; }

    /// <summary>Foreground colour for the description text. Defaults to the theme's foreground.</summary>
    public Color? LabelForeground { get; set; }

    /// <summary>The formatted key string, e.g. "Ctrl+S" or "F5".</summary>
    public string KeyText => Binding is null ? string.Empty : FormatKey(Binding);

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (Binding is null) return;

        var colorScheme = ThemeService.CurrentColorScheme;
        var capFg = KeyForeground ?? colorScheme.SelectedFocusedForeground;
        var descFg = LabelForeground ?? colorScheme.Foreground;
        var bg = colorScheme.Background;

        var cap = "[" + KeyText + "]";
        var line = new ConxelString(cap, capFg, bg);

        if (!string.IsNullOrEmpty(Label))
        {
            line.Append(" " + Label, descFg, bg);
        }

        // Pad to full view width so the background is uniform.
        var remaining = (int)ViewSize.Width - line.Count;
        if (remaining > 0)
        {
            line.Append(new string(' ', remaining), descFg, bg);
        }

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }

    private static string FormatKey(Key key)
    {
        var parts = new List<string>();
        if (key.Control) parts.Add("Ctrl");
        if (key.Alt)     parts.Add("Alt");
        if (key.Shift)   parts.Add("Shift");
        parts.Add(FormatConsoleKey(key.ConsoleKey));
        return string.Join("+", parts);
    }

    private static string FormatConsoleKey(ConsoleKey key) => key switch
    {
        ConsoleKey.UpArrow    => "↑",
        ConsoleKey.DownArrow  => "↓",
        ConsoleKey.LeftArrow  => "←",
        ConsoleKey.RightArrow => "→",
        ConsoleKey.Enter      => "Enter",
        ConsoleKey.Escape     => "Esc",
        ConsoleKey.Spacebar   => "Space",
        ConsoleKey.Tab        => "Tab",
        ConsoleKey.Backspace  => "Bksp",
        ConsoleKey.Delete     => "Del",
        ConsoleKey.Home       => "Home",
        ConsoleKey.End        => "End",
        ConsoleKey.PageUp     => "PgUp",
        ConsoleKey.PageDown   => "PgDn",
        ConsoleKey.Insert     => "Ins",
        _ => key.ToString()
    };
}
