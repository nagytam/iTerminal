using iConsole.Data;
using iConsole.Services;

namespace iConsole.Helper;

/// <summary>What a key means to a text control's clipboard.</summary>
public enum ClipboardKeyAction
{
    None,
    Copy,
    Cut,
    Paste,
    SelectAll,
}

/// <summary>
/// The clipboard keys of the text controls: the Ctrl chords (C, X, V, A) and the older CUA ones that no Ctrl letter
/// has taken (Ctrl+Insert copies, Shift+Delete cuts, Shift+Insert pastes). A control acts on one only when it can —
/// Copy and Cut with a selection, Paste when it may be changed — and otherwise leaves the key alone, so Ctrl+C still
/// means what it meant elsewhere (a file copy on a panel, quit) when there is nothing to copy.
/// </summary>
public static class ClipboardKeys
{
    /// <summary>How long a control waits for the system clipboard before giving up on one copy or paste.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(1500);

    public static ClipboardKeyAction ActionFor(KeyEvent keyEvent)
    {
        if (keyEvent.Alt)
        {
            return ClipboardKeyAction.None;
        }

        if (keyEvent.Control && !keyEvent.Shift)
        {
            return keyEvent.ConsoleKey switch
            {
                ConsoleKey.C or ConsoleKey.Insert => ClipboardKeyAction.Copy,
                ConsoleKey.X => ClipboardKeyAction.Cut,
                ConsoleKey.V => ClipboardKeyAction.Paste,
                ConsoleKey.A => ClipboardKeyAction.SelectAll,
                _ => ClipboardKeyAction.None,
            };
        }

        if (keyEvent.Shift && !keyEvent.Control)
        {
            return keyEvent.ConsoleKey switch
            {
                ConsoleKey.Delete => ClipboardKeyAction.Cut,
                ConsoleKey.Insert => ClipboardKeyAction.Paste,
                _ => ClipboardKeyAction.None,
            };
        }

        return ClipboardKeyAction.None;
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard; false when the clipboard could not be reached in time.</summary>
    public static async Task<bool> TrySetAsync(ITextClipboardService clipboard, string text)
    {
        using var timeout = new CancellationTokenSource(Patience);
        try
        {
            await clipboard.SetTextAsync(text, timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The clipboard's text, or null when there is none or the clipboard could not be reached in time.</summary>
    public static async Task<string?> TryGetAsync(ITextClipboardService clipboard)
    {
        using var timeout = new CancellationTokenSource(Patience);
        try
        {
            return await clipboard.GetTextAsync(timeout.Token);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Line breaks as a single line feed, whatever the other program wrote.</summary>
    public static string NormalizeLineBreaks(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
