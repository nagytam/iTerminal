using System.Text;

namespace iConsole.Services;

/// <summary>
/// Copies text to the clipboard of the machine the person is at, by asking the terminal to set it (OSC 52). It works
/// over SSH and without any clipboard program: the terminal receives the text in the output stream and sets its own
/// clipboard. It cannot read the clipboard, so it is write-only.
///
/// <para>The terminal must allow it. xterm, WezTerm, kitty, Windows Terminal and iTerm2 do by default; tmux needs
/// <c>set-clipboard on</c>. A terminal that ignores the sequence gives no error, so this is only ever the first choice when
/// there is no better one.</para>
///
/// <para>A copy is refused while the terminal is lent to a child process (the child owns the output), and a text too large
/// for the terminal's limit is refused rather than truncated.</para>
/// </summary>
public sealed class Osc52TextClipboardService : ITextClipboardService
{
    /// <summary>The largest encoded payload sent. Many terminals cap the sequence at about 100 000 characters.</summary>
    public const int MaxEncodedLength = 100_000;

    private readonly Func<TextWriter> _output;
    private readonly ConsoleOwnership? _ownership;

    /// <param name="output">Where the sequence is written. Defaults to standard output, which is the terminal.</param>
    /// <param name="ownership">The terminal's ownership. When the terminal is lent, the copy is refused.</param>
    public Osc52TextClipboardService(Func<TextWriter>? output = null, ConsoleOwnership? ownership = null)
    {
        _output = output ?? (() => Console.Out);
        _ownership = ownership;
    }

    /// <summary>The sequence that sets the clipboard to <paramref name="text"/>: OSC 52, the clipboard selection, base64, then BEL.</summary>
    public static string EncodeSequence(string text)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")));
        return "\u001b]52;c;" + payload + "\u0007";
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var sequence = EncodeSequence(text);
        var payloadLength = sequence.Length - "\u001b]52;c;".Length - 1;
        if (payloadLength > MaxEncodedLength)
        {
            throw new IOException($"The text is too large for the terminal clipboard ({payloadLength} characters encoded; the limit is {MaxEncodedLength}).");
        }

        // The terminal is not ours while a child process has it: the write waits for the next pass, as a redraw does.
        if (_ownership is not null && !_ownership.TryBeginUse())
        {
            throw new IOException("The terminal is in use by another program; the text was not copied to the terminal clipboard.");
        }

        try
        {
            // One write, so the sequence cannot be split by a redraw on another thread.
            var output = _output();
            output.Write(sequence);
            output.Flush();
        }
        finally
        {
            _ownership?.EndUse();
        }

        return Task.CompletedTask;
    }

    /// <summary>OSC 52 is write-only here: a read would need the terminal to answer on its input.</summary>
    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The terminal clipboard can be written but not read.");
}
