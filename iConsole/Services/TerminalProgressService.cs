namespace iConsole.Services;

/// <summary>Shows progress in the terminal's own tab or taskbar with OSC 9;4. What it is asked to show is remembered, so the
/// terminal can be given it again after a child process has had it.</summary>
public interface ITerminalProgressService
{
    /// <summary>Shows <paramref name="state"/> at <paramref name="percentage"/>. Writes nothing when the terminal does not take the sequence.</summary>
    void Update(TerminalProgressState state, long percentage);
}

/// <summary>
/// Writes OSC 9;4 to the terminal (see <see cref="TerminalProgressSequences"/>). It is a participant of
/// <see cref="ConsoleOwnership"/>: a write refused while the terminal is lent is not lost, because the last requested state is
/// written again when the terminal comes back. Nothing is written when the terminal does not take the sequence, or when its
/// output is redirected.
/// </summary>
public sealed class TerminalProgressService : ITerminalProgressService, IConsoleHandoverParticipant
{
    private readonly Func<TextWriter> _output;
    private readonly ConsoleOwnership? _ownership;
    private readonly bool _enabled;
    private readonly object _gate = new();
    private (TerminalProgressState State, long Percentage)? _requested;

    /// <param name="output">Where the sequences are written. Defaults to standard output, which is the terminal.</param>
    /// <param name="ownership">The terminal's ownership. While it is lent the state is only remembered.</param>
    /// <param name="enabled">Null reads <c>ITERMINAL_PROGRESS</c>, <c>WT_SESSION</c> and whether standard output is redirected.</param>
    public TerminalProgressService(Func<TextWriter>? output = null, ConsoleOwnership? ownership = null, bool? enabled = null)
    {
        _output = output ?? (() => Console.Out);
        _ownership = ownership;
        _enabled = enabled ?? (TerminalProgressSequences.SupportsProgress(Environment.GetEnvironmentVariable) && !Console.IsOutputRedirected);
        _ownership?.AddParticipant(this);
    }

    public void Update(TerminalProgressState state, long percentage)
    {
        lock (_gate)
        {
            _requested = (state, percentage);
            if (_enabled)
            {
                TerminalSequences.TryWrite(_output, _ownership, TerminalProgressSequences.Progress(state, percentage));
            }
        }
    }

    /// <summary>
    /// Gives the terminal back its last requested state. The lend is still in force while this runs, so the write skips the
    /// ownership check: a participant is called with the terminal held exclusively, which is what makes the write safe.
    /// </summary>
    public void OnConsoleReacquired()
    {
        lock (_gate)
        {
            if (_enabled && _requested is { } requested)
            {
                TerminalSequences.TryWrite(_output, ownership: null, TerminalProgressSequences.Progress(requested.State, requested.Percentage));
            }
        }
    }
}
