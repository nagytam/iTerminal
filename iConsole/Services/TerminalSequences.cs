using System.Text;

namespace iConsole.Services;

/// <summary>How the terminal is asked to tell the person something while they are looking elsewhere.</summary>
public enum TerminalNotificationStyle
{
    /// <summary>Nothing is sent.</summary>
    None,

    /// <summary>The bell (BEL). Every terminal knows it; many flash the tab or the taskbar button.</summary>
    Bell,

    /// <summary>OSC 9: one text. iTerm2, Windows Terminal, Ghostty and ConEmu.</summary>
    Osc9,

    /// <summary>OSC 777 <c>notify</c>: a title and a text. VTE terminals, Konsole, WezTerm and foot.</summary>
    Osc777
}

/// <summary>
/// The escape sequences for the terminal's title and its desktop notifications, and the rules for which terminal gets
/// which. Pure functions: the services write what these return.
/// </summary>
public static class TerminalSequences
{
    /// <summary>The longest title sent. Terminals cut a long title anyway, and some refuse it.</summary>
    public const int MaxTitleLength = 128;

    /// <summary>The longest notification text sent.</summary>
    public const int MaxNotificationLength = 256;

    /// <summary>CSI 22 ; 2 t: save the window title on the terminal's stack, so <see cref="PopTitle"/> can put it back.</summary>
    public const string PushTitle = "\u001b[22;2t";

    /// <summary>CSI 23 ; 2 t: restore the window title saved by <see cref="PushTitle"/>.</summary>
    public const string PopTitle = "\u001b[23;2t";

    public const string Bell = "\u0007";

    /// <summary>OSC 2: set the window title. BEL ends it, which every terminal accepts.</summary>
    public static string SetTitle(string title) => "\u001b]2;" + Sanitize(title, MaxTitleLength, keepSemicolons: true) + "\u0007";

    /// <summary>
    /// The sequence that shows a notification, or an empty string when <paramref name="style"/> sends none. The text is
    /// stripped of control characters, and the semicolons are replaced: OSC 777 splits its fields on them, and OSC 9
    /// followed by <c>4;</c> is Windows Terminal's progress indicator, so a text that began with a number and a semicolon
    /// would move the taskbar bar instead of showing a message.
    /// </summary>
    public static string Notification(TerminalNotificationStyle style, string title, string? body)
    {
        string cleanTitle = Sanitize(title, MaxNotificationLength, keepSemicolons: false);
        string cleanBody = Sanitize(body ?? string.Empty, MaxNotificationLength, keepSemicolons: false);
        switch (style)
        {
            case TerminalNotificationStyle.Bell:
                return Bell;
            case TerminalNotificationStyle.Osc9:
                string text = cleanBody.Length == 0 ? cleanTitle : cleanTitle.Length == 0 ? cleanBody : cleanTitle + ": " + cleanBody;
                return "\u001b]9;" + text + "\u0007";
            case TerminalNotificationStyle.Osc777:
                return "\u001b]777;notify;" + cleanTitle + ";" + cleanBody + "\u0007";
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Picks the notification style from the environment. <c>ITERMINAL_NOTIFY</c> overrides it (<c>off</c>, <c>bell</c>,
    /// <c>osc9</c>, <c>osc777</c>). Otherwise it is guessed from the terminal; one that is not recognised, or that sits behind
    /// tmux or screen (which swallow the sequences), gets the bell.
    /// </summary>
    public static TerminalNotificationStyle DetectNotificationStyle(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        string? forced = getEnvironmentVariable("ITERMINAL_NOTIFY")?.Trim().ToLowerInvariant();
        switch (forced)
        {
            case "off" or "0" or "none":
                return TerminalNotificationStyle.None;
            case "bell":
                return TerminalNotificationStyle.Bell;
            case "osc9":
                return TerminalNotificationStyle.Osc9;
            case "osc777":
                return TerminalNotificationStyle.Osc777;
        }

        string? term = getEnvironmentVariable("TERM");
        if (!string.IsNullOrEmpty(getEnvironmentVariable("TMUX")) || term is "dumb" or "linux" || term?.StartsWith("screen", StringComparison.Ordinal) == true)
        {
            return TerminalNotificationStyle.Bell;
        }

        string? program = getEnvironmentVariable("TERM_PROGRAM");
        if (!string.IsNullOrEmpty(getEnvironmentVariable("WT_SESSION")) || program is "iTerm.app" or "ghostty")
        {
            return TerminalNotificationStyle.Osc9;
        }

        if (program is "WezTerm"
            || !string.IsNullOrEmpty(getEnvironmentVariable("VTE_VERSION"))
            || !string.IsNullOrEmpty(getEnvironmentVariable("KONSOLE_VERSION"))
            || term?.StartsWith("foot", StringComparison.Ordinal) == true)
        {
            return TerminalNotificationStyle.Osc777;
        }

        return TerminalNotificationStyle.Bell;
    }

    /// <summary>
    /// Takes the control characters out of a text that goes inside an escape sequence (ESC and BEL would end it early, and
    /// anything after them would be read as input to the terminal), turns line breaks into spaces, and cuts it to
    /// <paramref name="maxLength"/> without splitting a surrogate pair.
    /// </summary>
    public static string Sanitize(string text, int maxLength, bool keepSemicolons)
    {
        ArgumentNullException.ThrowIfNull(text);

        maxLength = Math.Max(0, maxLength);
        var sb = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (char c in text)
        {
            if (sb.Length >= maxLength)
            {
                break;
            }

            if (c is '\r' or '\n' or '\t')
            {
                sb.Append(' ');
            }
            else if (char.IsControl(c))
            {
                continue;
            }
            else if (c == ';' && !keepSemicolons)
            {
                sb.Append(',');
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0 && char.IsHighSurrogate(sb[^1]))
        {
            sb.Length--;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Writes one sequence to the terminal in a single call, unless a child process has the terminal (then nothing is
    /// written and the result is false). Never throws: a closed output is not worth stopping the application.
    /// </summary>
    internal static bool TryWrite(Func<TextWriter> output, ConsoleOwnership? ownership, string sequence)
    {
        if (sequence.Length == 0)
        {
            return false;
        }

        if (ownership is not null && !ownership.TryBeginUse())
        {
            return false;
        }

        try
        {
            TextWriter writer = output();
            writer.Write(sequence);
            writer.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            ownership?.EndUse();
        }
    }
}
