using iConsole.Services;

namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// The text clipboard the application uses: the system clipboard, the terminal's clipboard through OSC 52, and the
/// application's own copy, tried in an order that suits where the application runs.
///
/// <para><b>Copy.</b> The application's own copy is always kept, so a paste inside the application agrees with the last
/// copy whatever happened outside it. Then the system clipboard is tried, and the terminal's after it. Over SSH the
/// terminal comes first: a clipboard program on the remote machine would set a clipboard the person cannot reach.</para>
///
/// <para><b>Paste.</b> The system clipboard, and when it holds no text (or there is none to read) the last text this
/// application copied. The terminal cannot be read, so it takes no part.</para>
///
/// <para>A system clipboard that has failed once is not asked again for the rest of the run, so a machine without a
/// clipboard tool does not start three processes on every copy.</para>
/// </summary>
public sealed class LayeredTextClipboardService(
    ITextClipboardService system,
    ITextClipboardService terminal,
    ITextClipboardService inProcess,
    bool preferTerminal) : ITextClipboardService
{
    private volatile bool _systemFailed;

    /// <summary>True when the application runs in a session that came over SSH, which is where the terminal's clipboard matters most.</summary>
    public static bool IsRemoteSession(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        return !string.IsNullOrEmpty(environment("SSH_CONNECTION"))
            || !string.IsNullOrEmpty(environment("SSH_TTY"))
            || !string.IsNullOrEmpty(environment("SSH_CLIENT"));
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        await inProcess.SetTextAsync(text, cancellationToken);

        if (preferTerminal)
        {
            if (await TryTerminalAsync(text, cancellationToken))
            {
                return;
            }
        }

        if (!_systemFailed && await TrySystemAsync(text, cancellationToken))
        {
            return;
        }

        if (!preferTerminal)
        {
            await TryTerminalAsync(text, cancellationToken);
        }
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        if (!_systemFailed)
        {
            try
            {
                var text = await system.GetTextAsync(cancellationToken);
                if (text is not null)
                {
                    return text;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _systemFailed = true;
            }
        }

        return await inProcess.GetTextAsync(cancellationToken);
    }

    private async Task<bool> TrySystemAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await system.SetTextAsync(text, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _systemFailed = true;
            return false;
        }
    }

    private async Task<bool> TryTerminalAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await terminal.SetTextAsync(text, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Refused (the terminal is lent, or the text is too large) or unsupported: the next layer answers.
            return false;
        }
    }
}
