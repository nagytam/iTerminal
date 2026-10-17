using System.Diagnostics;
using System.Globalization;

namespace iTerminal.Core.Audio.Decoders;

/// <summary>
/// Decodes anything ffmpeg understands (FLAC, OGG/Vorbis, Opus, M4A/AAC, WMA, AIFF, …) by piping
/// <c>ffmpeg -f s16le -</c> into the same PCM contract the managed decoders use. This keeps the
/// format list open-ended without bundling a codec zoo, at the price of an optional external tool:
/// when ffmpeg is missing the factory simply reports the file as unplayable and the UI says so.
///
/// <para>Seeking restarts the process with <c>-ss</c> — cheaper and far more reliable than trying
/// to discard decoded bytes, and ffmpeg's input seek is frame-accurate enough for a player.</para>
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioDecoder
{
    private const string FfmpegExecutable = "ffmpeg";
    private const string FfprobeExecutable = "ffprobe";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static bool? _isAvailable;

    private readonly string _path;
    private readonly object _processLock = new();

    private Process? _process;
    private Stream? _output;
    private long _bytesRead;
    private long _startOffsetBytes;
    private bool _disposed;

    public PcmFormat Format { get; } = PcmFormat.Default;

    public TimeSpan Duration { get; }

    public bool CanSeek => true;

    public TimeSpan Position => Format.BytesToTime(_startOffsetBytes + _bytesRead);

    public FfmpegAudioDecoder(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        if (!IsAvailable())
        {
            throw new InvalidOperationException("ffmpeg is not installed or not on PATH.");
        }

        Duration = ProbeDuration(path);
        Start(TimeSpan.Zero);
    }

    /// <summary>
    /// Whether an <c>ffmpeg</c> binary can be launched. Probed once per process and cached — the
    /// result gates the extra formats offered by <see cref="AudioDecoderFactory"/>.
    /// </summary>
    public static bool IsAvailable()
    {
        if (_isAvailable is bool cached)
        {
            return cached;
        }

        var available = TryRun(FfmpegExecutable, ["-version"], out _);
        _isAvailable = available;
        return available;
    }

    /// <summary>Forgets the cached probe, so the next <see cref="IsAvailable"/> looks again. Called after ffmpeg is installed.</summary>
    internal static void ResetAvailability() => _isAvailable = null;

    /// <summary>Test seam: forces the cached availability result.</summary>
    internal static void SetAvailabilityForTest(bool? available) => _isAvailable = available;

    public int Read(Span<byte> buffer)
    {
        var output = _output;
        if (output is null || buffer.Length <= 0)
        {
            return 0;
        }

        int read;
        try
        {
            read = output.Read(buffer);
        }
        catch
        {
            return 0;
        }

        if (read <= 0)
        {
            return 0;
        }

        _bytesRead += read;
        return read;
    }

    public bool TrySeek(TimeSpan position)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            Start(position < TimeSpan.Zero ? TimeSpan.Zero : position);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Start(TimeSpan offset)
    {
        lock (_processLock)
        {
            StopProcess();

            var startInfo = new ProcessStartInfo
            {
                FileName = FfmpegExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            // -nostdin stops ffmpeg from consuming the terminal's stdin, which would fight the
            // app's own keyboard input source for keystrokes.
            startInfo.ArgumentList.Add("-nostdin");
            if (offset > TimeSpan.Zero)
            {
                startInfo.ArgumentList.Add("-ss");
                startInfo.ArgumentList.Add(offset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(_path);
            startInfo.ArgumentList.Add("-vn");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("s16le");
            startInfo.ArgumentList.Add("-acodec");
            startInfo.ArgumentList.Add("pcm_s16le");
            startInfo.ArgumentList.Add("-ar");
            startInfo.ArgumentList.Add(Format.SampleRate.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-ac");
            startInfo.ArgumentList.Add(Format.Channels.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-");

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start ffmpeg.");

            // stderr is redirected to keep it off the rendered screen; drain it so a chatty ffmpeg
            // cannot fill the pipe buffer and deadlock its own stdout writes.
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            _process = process;
            _output = process.StandardOutput.BaseStream;
            _startOffsetBytes = Format.TimeToBytes(offset);
            _bytesRead = 0;
        }
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        _output = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }

    private static TimeSpan ProbeDuration(string path)
    {
        if (!TryRun(FfprobeExecutable,
                ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path],
                out var output))
        {
            return TimeSpan.Zero;
        }

        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
               && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    private static bool TryRun(string fileName, IReadOnlyList<string> arguments, out string standardOutput)
    {
        standardOutput = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            standardOutput = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_processLock)
        {
            StopProcess();
        }
    }
}
