using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace iTerminal.Core.Services.Shell;

/// <summary>How a command in a session ended.</summary>
public enum ShellRunState
{
    /// <summary>The command finished and the session is still running.</summary>
    Completed,

    /// <summary>Esc or cancellation ended the session while the command ran.</summary>
    Interrupted,

    /// <summary>The shell exited (typed <c>exit</c>, <c>set -e</c> on a failure) or could not be written to.</summary>
    Ended,
}

/// <summary>What a command printed, how it ended, and where the shell was afterwards.</summary>
public sealed record ShellCommandResult(string Output, string Error, int ExitCode, string? Folder, ShellRunState State);

/// <summary>
/// One long-lived <c>sh</c> or <c>bash</c> with redirected stdio. Commands are written to its stdin, each
/// followed by a marker line; the session reads stdout and stderr until both markers arrive, so a command's
/// output is separated from the next one's and its exit status and working directory are known.
///
/// <para>The session does not take part in <see cref="iConsole.Services.ConsoleOwnership"/>: its pipes are
/// not the terminal, and it inherits the terminal only as its controlling tty. On Linux the app runs its
/// input in raw mode, so Ctrl+C and Ctrl+Z are keys to the app, never signals to the session; Esc
/// (<see cref="Interrupt"/>) is the way to stop a command.</para>
///
/// <para>Known limits: an unterminated quote, an open heredoc or an unclosed block keeps the marker from
/// being read, so the run ends only on Esc; <c>exit</c> ends the session; a program that opens
/// <c>/dev/tty</c> bypasses the protocol.</para>
/// </summary>
public sealed class LocalShellSession : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ShellKind _kind;
    private readonly IShellProcess _process;
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly string _token = Guid.NewGuid().ToString("N");
    private volatile bool _killRequested;

    private LocalShellSession(ShellKind kind, IShellProcess process, string folder)
    {
        _kind = kind;
        _process = process;
        Folder = folder;
        _ = Pump(process.StandardOutput, _stdout.Writer);
        _ = Pump(process.StandardError, _stderr.Writer);
    }

    /// <summary>The working directory the shell reported after its last command (the folder it started in until then).</summary>
    public string Folder { get; private set; }

    public ShellKind Kind => _kind;

    /// <summary>
    /// Starts the shell in <paramref name="folder"/>, through <paramref name="sandbox"/> (which may wrap it in a
    /// confinement program). Throws when the program cannot be started.
    /// </summary>
    public static LocalShellSession Start(ShellKind kind, string programPath, string folder, ISessionSandbox sandbox)
    {
        var shell = new SessionLaunch(programPath, ShellKinds.LaunchArguments(kind));
        var existingFolder = Directory.Exists(folder) ? folder : string.Empty;

        // A sandbox that creates the process itself: no command to rewrite, and no unconfined fallback here.
        var owned = sandbox.Start(shell, folder);
        if (owned is not null)
        {
            return new LocalShellSession(kind, owned, existingFolder);
        }

        var launch = sandbox.Wrap(shell, folder);
        var info = new ProcessStartInfo(launch.Program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (Directory.Exists(folder))
        {
            info.WorkingDirectory = folder;
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException($"{programPath} did not start.");
        return new LocalShellSession(kind, new ChildShellProcess(process), existingFolder);
    }

    /// <summary>True once the shell process has gone.</summary>
    public bool HasEnded => _process.HasExited;

    /// <summary>
    /// Runs <paramref name="command"/> in the shell and returns when its marker has been read on both streams,
    /// the shell has ended, or <paramref name="cancellationToken"/> is cancelled (which ends the session).
    /// </summary>
    public async Task<ShellCommandResult> RunAsync(string command, CancellationToken cancellationToken = default)
    {
        if (HasEnded)
        {
            return new ShellCommandResult(string.Empty, string.Empty, -1, Folder, ShellRunState.Ended);
        }

        try
        {
            var text = ShellKinds.Wrap(command) + "\n" + ShellKinds.MarkerLine(_kind, _token) + "\n";
            await _process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);

            var stdout = ReadUntilMarker(_stdout.Reader, isStdout: true, cancellationToken);
            var stderr = ReadUntilMarker(_stderr.Reader, isStdout: false, cancellationToken);
            await Task.WhenAll(stdout, stderr);

            var out1 = await stdout;
            var err1 = await stderr;
            if (out1.Ended || err1.Ended)
            {
                return new ShellCommandResult(Join(out1.Lines), Join(err1.Lines), -1, Folder,
                    _killRequested ? ShellRunState.Interrupted : ShellRunState.Ended);
            }

            if (!string.IsNullOrEmpty(out1.Folder))
            {
                Folder = out1.Folder;
            }

            return new ShellCommandResult(Join(out1.Lines), Join(err1.Lines), out1.ExitCode, Folder, ShellRunState.Completed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interrupt();
            return new ShellCommandResult(string.Empty, string.Empty, -1, Folder, ShellRunState.Interrupted);
        }
        catch (IOException)
        {
            // The shell went away while the command was being written.
            return new ShellCommandResult(string.Empty, string.Empty, -1, Folder, _killRequested ? ShellRunState.Interrupted : ShellRunState.Ended);
        }
    }

    /// <summary>Kills the shell and everything it started. Non-blocking: the run in progress ends when the streams close.</summary>
    public void Interrupt()
    {
        _killRequested = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    public void Dispose()
    {
        Interrupt();
        try
        {
            _process.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
            // Intentionally empty: there is nothing to do for this case.
        }

        _process.Dispose();
    }

    private sealed record StreamRead(List<string> Lines, int ExitCode, string? Folder, bool Ended);

    /// <summary>
    /// Reads lines until the one carrying this session's token. Text before the token on that line is output
    /// (a command may end without a newline); on stdout the token is followed by the exit status and the
    /// working directory, on stderr by the status alone.
    /// </summary>
    private async Task<StreamRead> ReadUntilMarker(ChannelReader<string> reader, bool isStdout, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var line))
            {
                var index = line.IndexOf(_token, StringComparison.Ordinal);
                if (index < 0)
                {
                    lines.Add(line);
                    continue;
                }

                var before = line[..index];
                if (before.Length > 0)
                {
                    lines.Add(before);
                }

                var rest = line[(index + _token.Length)..].Trim();
                var space = rest.IndexOf(' ');
                var status = space < 0 ? rest : rest[..space];
                var folder = isStdout && space >= 0 ? rest[(space + 1)..] : null;
                if (!int.TryParse(status, out var exitCode))
                {
                    exitCode = -1;
                }

                return new StreamRead(lines, exitCode, folder, false);
            }
        }

        return new StreamRead(lines, -1, null, true);
    }

    private static async Task Pump(TextReader reader, ChannelWriter<string> writer)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                await writer.WriteAsync(line);
            }
        }
        catch (IOException)
        {
            // The process was killed or closed its pipe.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static string Join(List<string> lines) => string.Join('\n', lines);
}
