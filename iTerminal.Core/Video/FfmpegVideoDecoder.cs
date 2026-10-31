using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using iTerminal.Core.Audio.Decoders;

namespace iTerminal.Core.Video;

/// <summary>
/// Decodes video by piping <c>ffmpeg -f rawvideo -pix_fmt rgb24 -</c>. ffmpeg also does the scaling
/// down to the character grid and the frame-rate cap, so what arrives here is a few kilobytes per
/// frame rather than a full-resolution picture. Seeking restarts the process with <c>-ss</c>, the
/// same trade the audio decoder makes: simpler and far more reliable than discarding frames.
/// </summary>
public sealed class FfmpegVideoDecoder : IVideoDecoder
{
    private const string FfmpegExecutable = "ffmpeg";
    private const string FfprobeExecutable = "ffprobe";

    /// <summary>
    /// The ceiling on the playback rate. A terminal cannot repaint much faster, and every frame
    /// ffmpeg is not asked for is a frame this app does not have to convert and draw.
    /// </summary>
    public const double MaxFrameRate = 24;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static bool? _isProbeAvailable;

    /// <summary>
    /// Whether <c>ffprobe</c> can be launched. It ships in the same package as ffmpeg but is a separate program, and
    /// the viewers need the picture size from it. Probed once and cached, like the audio decoder's check.
    /// </summary>
    public static bool IsProbeAvailable()
    {
        if (_isProbeAvailable is bool cached)
        {
            return cached;
        }

        var available = TryRun(FfprobeExecutable, ["-version"], out _);
        _isProbeAvailable = available;
        return available;
    }

    internal static void ResetProbeAvailability() => _isProbeAvailable = null;

    private readonly string _path;
    private readonly object _processLock = new();

    private Process? _process;
    private Stream? _output;
    private bool _disposed;

    public VideoInfo Info { get; }

    /// <summary>The rate frames are actually delivered at: the source's, capped at <see cref="MaxFrameRate"/>.</summary>
    public double PlaybackFrameRate => Math.Clamp(Info.FrameRate, 1, MaxFrameRate);

    public FfmpegVideoDecoder(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        Info = Probe(path);
    }

    public void Open(TimeSpan offset, int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (columns <= 0 || rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "The frame size must be positive.");
        }

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
            // Keeps ffmpeg off the terminal's stdin, which the app's own input source reads.
            startInfo.ArgumentList.Add("-nostdin");
            if (offset > TimeSpan.Zero)
            {
                startInfo.ArgumentList.Add("-ss");
                startInfo.ArgumentList.Add(offset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(_path);
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"scale={columns}:{rows}:flags=area,fps={PlaybackFrameRate:0.###}"));
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("rgb24");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("rawvideo");
            startInfo.ArgumentList.Add("-");

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start ffmpeg.");

            // Drained so a chatty ffmpeg cannot fill the pipe and stall its own stdout writes.
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            _process = process;
            _output = process.StandardOutput.BaseStream;
        }
    }

    public bool ReadFrame(byte[] rgb)
    {
        var output = _output;
        if (output is null || rgb.Length == 0)
        {
            return false;
        }

        try
        {
            var filled = 0;
            while (filled < rgb.Length)
            {
                var read = output.Read(rgb, filled, rgb.Length - filled);
                if (read <= 0)
                {
                    return false;
                }

                filled += read;
            }

            return true;
        }
        catch
        {
            // The process was killed under the read by a reopen or a dispose.
            return false;
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

    private static VideoInfo Probe(string path)
    {
        if (!TryRun(
                FfprobeExecutable,
                [
                    "-v", "error", "-select_streams", "v:0",
                    "-show_entries", "stream=width,height,avg_frame_rate,r_frame_rate,duration:format=duration",
                    "-of", "json", path,
                ],
                out var json))
        {
            throw new InvalidOperationException("ffprobe could not read the file. Is ffmpeg installed and on PATH?");
        }

        return ParseProbe(json);
    }

    /// <summary>Reads the JSON <c>ffprobe</c> prints. Exposed so the parsing can be tested without ffmpeg.</summary>
    internal static VideoInfo ParseProbe(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array
            || streams.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("The file has no video stream.");
        }

        var stream = streams[0];
        var width = stream.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
        var height = stream.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : 0;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The video stream has no picture size.");
        }

        var frameRate = ParseRate(ReadString(stream, "avg_frame_rate"));
        if (frameRate <= 0)
        {
            frameRate = ParseRate(ReadString(stream, "r_frame_rate"));
        }

        if (frameRate <= 0)
        {
            frameRate = 25;
        }

        var seconds = ParseSeconds(ReadString(stream, "duration"));
        if (seconds <= 0 && root.TryGetProperty("format", out var format))
        {
            seconds = ParseSeconds(ReadString(format, "duration"));
        }

        return new VideoInfo(width, height, seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero, frameRate);
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double ParseSeconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;

    /// <summary>ffprobe writes a rate as a fraction, "30000/1001"; "0/0" means unknown.</summary>
    private static double ParseRate(string text)
    {
        var parts = text.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0)
        {
            return numerator / denominator;
        }

        return parts.Length == 1 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain)
            ? plain
            : 0;
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

            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
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

            standardOutput = output.GetAwaiter().GetResult();
            _ = errors.GetAwaiter().GetResult();
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

public sealed class FfmpegVideoDecoderFactory : IVideoDecoderFactory
{
    public static string MissingMessage => iConsole.Localization.UiText.Get("Ffmpeg.Missing", "This needs ffmpeg and ffprobe on PATH — Tools > Runtime Dependencies installs them.");

    public bool IsAvailable => FfmpegAudioDecoder.IsAvailable() && FfmpegVideoDecoder.IsProbeAvailable();

    public IVideoDecoder Create(string path)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(MissingMessage);
        }

        return new FfmpegVideoDecoder(path);
    }
}
