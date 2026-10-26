namespace iConsole.Services;

/// <summary>Reaches the person through the terminal when they are looking at another window: the bell and desktop notifications.</summary>
public interface ITerminalNotificationService
{
    /// <summary>What is sent, as detected from the terminal or forced by <c>ITERMINAL_NOTIFY</c>.</summary>
    TerminalNotificationStyle Style { get; }

    /// <summary>Rings the terminal bell.</summary>
    void Bell();

    /// <summary>
    /// Asks the terminal to show a notification. A terminal with no notification sequence gets the bell instead; with the
    /// style <see cref="TerminalNotificationStyle.None"/> nothing is sent. Returns whether anything was written.
    /// </summary>
    bool Notify(string title, string? body = null);
}
