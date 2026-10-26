namespace iConsole.Controls;

/// <summary>One entry of a <see cref="ContextMenu"/>: a title, an optional key hint, and what it does when chosen.</summary>
public class ContextMenuItem
{
    public ContextMenuItem(string title)
    {
        Title = title ?? string.Empty;
    }

    /// <summary>A line between two groups of entries; it can not be chosen.</summary>
    public static ContextMenuItem Separator() => new(string.Empty) { IsSeparator = true, IsEnabled = false };

    public string Title { get; set; }

    /// <summary>The key that does the same thing elsewhere ("F5", "Ctrl+C"), shown at the right edge. Only a hint.</summary>
    public string Shortcut { get; set; } = string.Empty;

    /// <summary>Run after the menu has closed and focus has gone back to where it was.</summary>
    public Func<Task>? Execute { get; set; }

    /// <summary>A disabled entry is drawn faint and is skipped by the keyboard.</summary>
    public bool IsEnabled { get; set; } = true;

    public bool IsSeparator { get; init; }

    /// <summary>Whether the keyboard can stop on this entry.</summary>
    public bool CanSelect => IsEnabled && !IsSeparator;
}
