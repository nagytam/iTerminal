using iTerminal.Core.Data.Music;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace iTerminal.Core.Audio.Sinks;

/// <summary>
/// Last-resort Linux output: raw PCM piped into <c>aplay</c>, the ALSA command-line player that is
/// present on essentially any system with sound, PulseAudio or not. Writing to a pipe blocks when
/// aplay's buffer is full, so it satisfies the pump's clock contract just like the native sinks.
///
/// <para>Device selection maps to <c>aplay -D</c>, so the ALSA device strings from <c>aplay -L</c>
/// ("default", "plughw:1,0", …) work as selector entries.</para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class AplayAudioSink : IAudioSink
{
    private const string Executable = "aplay";

    private readonly object _syncLock = new();

    private Process? _process;
    private Stream? _input;
    private PcmFormat _format;
    private string? _deviceId;
    private volatile bool _stopped;

    /// <summary>
    /// aplay does not report its buffer fill, so this is a fixed estimate of the pipe plus ALSA
    /// buffer. It only shifts the displayed elapsed time slightly; playback itself is unaffected.
    /// </summary>
    public TimeSpan BufferedDuration => TimeSpan.FromMilliseconds(_process is null ? 0 : 250);

    public void Open(PcmFormat format, string? deviceId)
    {
        if (!format.IsValid)
        {
            throw new ArgumentException("Invalid PCM format.", nameof(format));
        }

        Close();

        lock (_syncLock)
        {
            _format = format;
            _deviceId = deviceId;
            _stopped = false;

            var startInfo = new ProcessStartInfo
            {
                FileName = Executable,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("-q");
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add("raw");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("S16_LE");
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add(format.SampleRate.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(format.Channels.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                startInfo.ArgumentList.Add("-D");
                startInfo.ArgumentList.Add(deviceId);
            }
            startInfo.ArgumentList.Add("-");

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start aplay.");

            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            _process = process;
            _input = process.StandardInput.BaseStream;
        }
    }

    public int Write(ReadOnlySpan<byte> pcm)
    {
        var input = _input;
        if (input is null || pcm.Length <= 0 || _stopped)
        {
            return 0;
        }

        try
        {
            input.Write(pcm);
            input.Flush();
            return pcm.Length;
        }
        catch
        {
            return 0;
        }
    }

    public void Drain()
    {
        try
        {
            _input?.Flush();
        }
        catch
        {
        }
    }

    /// <summary>
    /// aplay cannot discard buffered audio, so a flush restarts it. That is the only way to make a
    /// seek audible immediately rather than after the existing buffer has played out.
    /// </summary>
    public void Flush()
    {
        if (_process is null)
        {
            return;
        }

        var format = _format;
        var deviceId = _deviceId;
        Close();
        if (!_stopped)
        {
            Open(format, deviceId);
        }
    }

    public void Stop()
    {
        _stopped = true;
        Close();
    }

    /// <summary>No-op: the pump simply stops feeding the pipe, which stalls aplay within a buffer.</summary>
    public void Pause()
    {
    }

    public void Resume()
    {
    }

    private void Close()
    {
        lock (_syncLock)
        {
            var process = _process;
            var input = _input;
            _process = null;
            _input = null;

            try
            {
                input?.Dispose();
            }
            catch
            {
            }

            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.WaitForExit(300) && !process.HasExited)
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
    }

    public void Dispose()
    {
        _stopped = true;
        Close();
    }

    internal static bool IsAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return RunLines(["--version"]).Count > 0;
    }

    /// <summary>
    /// Parses <c>aplay -L</c>, whose output is a device name on a flush-left line followed by
    /// indented description lines. Only the plug/default entries are offered: raw "hw:" devices
    /// reject most sample rates outright, which would look like a broken selector.
    /// </summary>
    internal static IReadOnlyList<AudioDevice> GetDevices()
    {
        var devices = new List<AudioDevice>
        {
            new("default", "Default output", "ALSA") { IsDefault = true },
        };

        string? pendingName = null;
        foreach (var line in RunLines(["-L"]))
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                if (pendingName is not null)
                {
                    devices.Add(new AudioDevice(pendingName, $"{line.Trim()} ({pendingName})", "ALSA"));
                    pendingName = null;
                }
                continue;
            }

            var name = line.Trim();
            pendingName = null;
            if (name is "default" or "null" || name.Length == 0)
            {
                continue;
            }

            if (name.StartsWith("plughw:", StringComparison.Ordinal)
                || name.StartsWith("sysdefault:", StringComparison.Ordinal)
                || name.StartsWith("pulse", StringComparison.Ordinal))
            {
                pendingName = name;
            }
        }

        return devices;
    }

    private static IReadOnlyList<string> RunLines(IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Executable,
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
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return [];
            }

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return [];
        }
    }
}
