using System.Diagnostics;
using System.Text;
using iConsole.Localization;

namespace iTerminal.Core.Speech;

/// <summary>One way of starting a recorder program: it writes raw 16 kHz mono s16le to its standard output.</summary>
internal sealed record RecorderCommand(string Program, string Arguments);

/// <summary>
/// Records through a child program (<c>parecord</c> or <c>arecord</c> on Linux, <c>ffmpeg</c> on macOS). Its standard
/// streams are all redirected, so it never touches the terminal, and the process is killed on <see cref="Stop"/>,
/// on <see cref="Dispose"/> and so on shutdown: a recorder left running would keep the microphone open.
/// </summary>
internal sealed class ProcessMicrophoneCapture(IReadOnlyList<RecorderCommand> candidates) : IMicrophoneCapture
{
    /// <summary>How long a recorder that cannot work has to fail in: a missing device or permission ends it at once.</summary>
    private static readonly TimeSpan StartupProbe = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(1);

    private readonly PcmBuffer _buffer = new();
    private readonly StringBuilder _errors = new();
    private Process? _process;
    private Task? _reader;

    /// <summary>The id of the recorder while it runs; tests check that it is gone after <see cref="Stop"/>.</summary>
    internal int? ProcessId => _process?.Id;

    internal static string ParecordArguments() => "--raw --format=s16le --rate=16000 --channels=1 --latency-msec=100";

    internal static string ArecordArguments() => "-q -t raw -f S16_LE -r 16000 -c 1";

    internal static string FfmpegArguments(string device) =>
        $"-hide_banner -loglevel error -nostdin -f avfoundation -i \"{device}\" -ar 16000 -ac 1 -f s16le -";

    public void Start()
    {
        string failure = string.Empty;
        foreach (RecorderCommand command in candidates)
        {
            _errors.Clear();
            Process process = new()
            {
                StartInfo = new ProcessStartInfo(command.Program, command.Arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (_errors)
                    {
                        _errors.AppendLine(e.Data);
                    }
                }
            };

            try
            {
                process.Start();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                process.Dispose();
                failure = exception.Message;
                continue;
            }

            process.BeginErrorReadLine();
            Stream output = process.StandardOutput.BaseStream;
            Task reader = Task.Run(() => Pump(output));

            if (process.WaitForExit(StartupProbe))
            {
                // Gone already: no device, no permission, an option this version does not know. Try the next form.
                reader.Wait(DrainTimeout);
                process.WaitForExit();
                lock (_errors)
                {
                    failure = _errors.ToString().Trim();
                }

                process.Dispose();
                continue;
            }

            _process = process;
            _reader = reader;
            return;
        }

        throw new SpeechException(Describe(failure));
    }

    public byte[] Stop()
    {
        Process? process = _process;
        _process = null;
        bool died = false;
        if (process is not null)
        {
            // A recorder that is gone before we stop it did not run as asked (a device or a permission that failed too late
            // for the start-up probe): say so instead of handing back a recording too short to use.
            died = process.HasExited && process.ExitCode != 0;
            Kill(process);
            try
            {
                _reader?.Wait(DrainTimeout);
            }
            catch (AggregateException)
            {
            }

            process.Dispose();
        }

        if (died)
        {
            string errors;
            lock (_errors)
            {
                errors = _errors.ToString().Trim();
            }

            throw new SpeechException(Describe(errors));
        }

        return _buffer.ToArray();
    }

    public void Dispose()
    {
        if (_process is { } process)
        {
            _process = null;
            Kill(process);
            process.Dispose();
        }
    }

    private void Pump(Stream output)
    {
        byte[] chunk = new byte[8192];
        try
        {
            int read;
            while ((read = output.Read(chunk, 0, chunk.Length)) > 0)
            {
                _buffer.Append(chunk.AsSpan(0, read));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The recorder was killed: what arrived is what there is.
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(2000);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>The words for a recorder that would not run, from what it printed.</summary>
    internal static string Describe(string recorderOutput) => Describe(recorderOutput, OperatingSystem.IsMacOS());

    /// <param name="isMac">The privacy advice names macOS's settings; elsewhere a refusal is a device the user may not open, and the recorder's own words say so.</param>
    internal static string Describe(string recorderOutput, bool isMac)
    {
        string lower = recorderOutput.ToLowerInvariant();
        if (isMac && (lower.Contains("not authorized") || lower.Contains("permission") || lower.Contains("denied")))
        {
            return UiText.Get(
                "Voice.MicDenied",
                "Microphone access was denied. Allow your terminal in System Settings > Privacy & Security > Microphone.");
        }

        string first = recorderOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return first.Length == 0
            ? UiText.Get("Voice.NoMicrophone", "No microphone was found.")
            : UiText.Get("Voice.RecorderFailed", "The recorder could not start:") + " " + first;
    }
}
