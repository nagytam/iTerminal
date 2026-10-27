namespace iConsole.Services;

/// <summary>The shape of the mouse pointer over the terminal window, by the name terminals know it as (OSC 22).</summary>
public enum PointerShape
{
    /// <summary>The terminal's own pointer.</summary>
    Default,

    /// <summary>The pointing hand: buttons and links.</summary>
    Pointer,

    /// <summary>The I-beam: text fields and the text of the editor.</summary>
    Text,

    /// <summary>A double arrow left and right: a divider that moves sideways.</summary>
    ResizeHorizontal,

    /// <summary>A double arrow up and down: a divider that moves up and down.</summary>
    ResizeVertical,

    /// <summary>Something that cannot be used now.</summary>
    NotAllowed,
}

/// <summary>Sets the pointer shape. Only a terminal that supports OSC 22 acts on it.</summary>
public interface IPointerShapeService
{
    /// <summary>The shape last asked for.</summary>
    PointerShape Shape { get; }

    /// <summary>Shows the shape. Asking for the one already shown writes nothing.</summary>
    void Set(PointerShape shape);

    /// <summary>Gives the pointer back to the terminal.</summary>
    void Reset();
}

/// <summary>
/// Writes <c>OSC 22 ; name ST</c> when the shape changes, and the default again on <see cref="Reset"/> or when disposed (the
/// container does that on shutdown), so the terminal is left as found. It is on only for terminals known to honour the sequence
/// (Kitty, Ghostty, WezTerm, foot) or when <c>ITERMINAL_POINTER_SHAPE=on</c>; <c>off</c> switches it off. Nothing is written while
/// a child process has the terminal.
/// </summary>
public sealed class PointerShapeService : IPointerShapeService, IDisposable
{
    private readonly Func<TextWriter> _output;
    private readonly ConsoleOwnership? _ownership;
    private readonly bool _enabled;
    private readonly Lock _gate = new();
    private PointerShape _shape;
    private bool _written;

    /// <param name="output">Where the sequences go; standard output by default.</param>
    /// <param name="ownership">While the terminal is lent nothing is written.</param>
    /// <param name="enabled">Null decides from the environment and from whether output is redirected.</param>
    public PointerShapeService(Func<TextWriter>? output = null, ConsoleOwnership? ownership = null, bool? enabled = null)
    {
        _output = output ?? (() => Console.Out);
        _ownership = ownership;
        _enabled = enabled ?? (DetectSupport(Environment.GetEnvironmentVariable) && !Console.IsOutputRedirected);
    }

    public PointerShape Shape
    {
        get { lock (_gate) { return _shape; } }
    }

    /// <summary>The name OSC 22 uses for a shape (the CSS cursor names).</summary>
    public static string NameOf(PointerShape shape) => shape switch
    {
        PointerShape.Pointer => "pointer",
        PointerShape.Text => "text",
        PointerShape.ResizeHorizontal => "ew-resize",
        PointerShape.ResizeVertical => "ns-resize",
        PointerShape.NotAllowed => "not-allowed",
        _ => "default",
    };

    /// <summary>The sequence that shows a shape.</summary>
    public static string Sequence(PointerShape shape) => "\u001b]22;" + NameOf(shape) + "\u001b\\";

    /// <summary>On for a terminal known to honour OSC 22, or when <c>ITERMINAL_POINTER_SHAPE</c> says <c>on</c>; off when it says <c>off</c>.</summary>
    public static bool DetectSupport(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        switch (getEnvironmentVariable("ITERMINAL_POINTER_SHAPE")?.Trim().ToLowerInvariant())
        {
            case "on" or "1":
                return true;
            case "off" or "0":
                return false;
        }

        string? term = getEnvironmentVariable("TERM");
        string? program = getEnvironmentVariable("TERM_PROGRAM");
        if (!string.IsNullOrEmpty(getEnvironmentVariable("TMUX")) || term?.StartsWith("screen", StringComparison.Ordinal) == true)
        {
            return false;
        }

        return !string.IsNullOrEmpty(getEnvironmentVariable("KITTY_WINDOW_ID"))
            || term is "xterm-kitty" or "xterm-ghostty"
            || term?.StartsWith("foot", StringComparison.Ordinal) == true
            || program is "ghostty" or "WezTerm";
    }

    public void Set(PointerShape shape)
    {
        lock (_gate)
        {
            if (!_enabled || shape == _shape)
            {
                return;
            }

            if (TerminalSequences.TryWrite(_output, _ownership, Sequence(shape)))
            {
                _shape = shape;
                _written = true;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (!_written)
            {
                return;
            }

            if (TerminalSequences.TryWrite(_output, _ownership, Sequence(PointerShape.Default)))
            {
                _shape = PointerShape.Default;
                _written = false;
            }
        }
    }

    public void Dispose() => Reset();
}
