namespace iConsole.Services;

/// <summary>
/// Sends the bell and desktop notifications as escape sequences (OSC 9 or OSC 777, chosen by
/// <see cref="TerminalSequences.DetectNotificationStyle"/>). They work over SSH, because the terminal on the person's own
/// machine receives them. Nothing is written while a child process has the terminal.
/// </summary>
public sealed class TerminalNotificationService : ITerminalNotificationService
{
    private readonly Func<TextWriter> _output;
    private readonly ConsoleOwnership? _ownership;

    /// <param name="output">Where the sequences are written. Defaults to standard output, which is the terminal.</param>
    /// <param name="ownership">The terminal's ownership. While it is lent nothing is written.</param>
    /// <param name="style">Null detects it from the environment; redirected output sends nothing.</param>
    public TerminalNotificationService(Func<TextWriter>? output = null, ConsoleOwnership? ownership = null, TerminalNotificationStyle? style = null)
    {
        _output = output ?? (() => Console.Out);
        _ownership = ownership;
        Style = style ?? (Console.IsOutputRedirected
            ? TerminalNotificationStyle.None
            : TerminalSequences.DetectNotificationStyle(Environment.GetEnvironmentVariable));
    }

    public TerminalNotificationStyle Style { get; }

    public void Bell()
    {
        if (Style != TerminalNotificationStyle.None)
        {
            TerminalSequences.TryWrite(_output, _ownership, TerminalSequences.Bell);
        }
    }

    public bool Notify(string title, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        return TerminalSequences.TryWrite(_output, _ownership, TerminalSequences.Notification(Style, title, body));
    }
}
