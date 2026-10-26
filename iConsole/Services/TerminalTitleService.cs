namespace iConsole.Services;

/// <summary>
/// Sets the terminal's title with OSC 2. The first change saves the title the terminal had (a window-title push), and
/// <see cref="Restore"/> brings it back, so quitting leaves the tab as it was found. Setting the title it already has writes
/// nothing. <c>ITERMINAL_TERMINAL_TITLE=0</c> switches it off.
/// </summary>
public sealed class TerminalTitleService : ITerminalTitleService
{
    private readonly Func<TextWriter> _output;
    private readonly ConsoleOwnership? _ownership;
    private readonly bool _enabled;
    private readonly object _gate = new();
    private string _title = string.Empty;
    private bool _pushed;

    /// <param name="output">Where the sequences are written. Defaults to standard output, which is the terminal.</param>
    /// <param name="ownership">The terminal's ownership. While it is lent the title is not written.</param>
    /// <param name="enabled">Null reads <c>ITERMINAL_TERMINAL_TITLE</c> and whether standard output is redirected.</param>
    public TerminalTitleService(Func<TextWriter>? output = null, ConsoleOwnership? ownership = null, bool? enabled = null)
    {
        _output = output ?? (() => Console.Out);
        _ownership = ownership;
        _enabled = enabled ?? (Environment.GetEnvironmentVariable("ITERMINAL_TERMINAL_TITLE") != "0" && !Console.IsOutputRedirected);
    }

    public string Title
    {
        get
        {
            lock (_gate)
            {
                return _title;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                string clean = TerminalSequences.Sanitize(value, TerminalSequences.MaxTitleLength, keepSemicolons: true);
                if (!_enabled || clean == _title)
                {
                    return;
                }

                string sequence = (_pushed ? string.Empty : TerminalSequences.PushTitle) + TerminalSequences.SetTitle(clean);
                if (TerminalSequences.TryWrite(_output, _ownership, sequence))
                {
                    _pushed = true;
                    _title = clean;
                }
            }
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            if (!_pushed)
            {
                return;
            }

            if (TerminalSequences.TryWrite(_output, _ownership, TerminalSequences.PopTitle))
            {
                _pushed = false;
                _title = string.Empty;
            }
        }
    }
}
