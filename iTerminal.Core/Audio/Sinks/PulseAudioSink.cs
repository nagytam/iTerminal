using iTerminal.Core.Data.Music;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace iTerminal.Core.Audio.Sinks;

/// <summary>
/// Linux output through <c>libpulse-simple</c>, which also covers PipeWire (its pulse shim is what
/// virtually every current desktop exposes). The simple API is a deliberate fit: it is synchronous
/// and blocking, so <c>pa_simple_write</c> paces the pump exactly like waveOut does on Windows,
/// without a mainloop or async callbacks.
///
/// <para>Device selection is by sink name — the same names <c>pactl list short sinks</c> prints —
/// passed straight to <c>pa_simple_new</c>. A null name means the server's default sink.</para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class PulseAudioSink : IAudioSink
{
    private const string LibPulseSimple = "libpulse-simple.so.0";
    private const int SampleFormatS16Le = 3;
    private const int StreamPlayback = 1;

    private readonly object _syncLock = new();

    private IntPtr _handle;
    private PcmFormat _format;
    private byte[] _writeScratch = [];
    private volatile bool _stopped;

    public TimeSpan BufferedDuration
    {
        get
        {
            lock (_syncLock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return TimeSpan.Zero;
                }

                try
                {
                    var latency = pa_simple_get_latency(_handle, out var error);
                    return error != 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(latency / 1000.0);
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }
    }

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
            _stopped = false;

            var spec = new SampleSpec
            {
                Format = SampleFormatS16Le,
                Rate = (uint)format.SampleRate,
                Channels = (byte)format.Channels,
            };

            // Cap the server-side buffer at ~200 ms. The default target latency runs into seconds,
            // which would make pause and track changes lag audibly behind the keystroke.
            var attributes = new BufferAttributes
            {
                MaxLength = uint.MaxValue,
                TargetLength = (uint)(format.BytesPerSecond / 5),
                PreBuffer = uint.MaxValue,
                MinimumRequest = uint.MaxValue,
                FragmentSize = uint.MaxValue,
            };

            var device = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
            _handle = pa_simple_new(
                null,
                "iTerminal",
                StreamPlayback,
                device,
                "Music player",
                ref spec,
                IntPtr.Zero,
                ref attributes,
                out var error);

            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not open PulseAudio sink (error {error}).");
            }

            _writeScratch = new byte[Math.Max(format.BlockAlign, format.BytesPerSecond / 10)];
        }
    }

    public int Write(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length <= 0 || _stopped)
        {
            return 0;
        }

        IntPtr handle;
        lock (_syncLock)
        {
            handle = _handle;
        }

        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            var written = 0;
            while (written < pcm.Length && !_stopped)
            {
                var chunk = Math.Min(_writeScratch.Length, pcm.Length - written);
                pcm.Slice(written, chunk).CopyTo(_writeScratch);
                if (pa_simple_write(handle, _writeScratch, (nuint)chunk, out _) < 0)
                {
                    break;
                }
                written += chunk;
            }

            return written;
        }
        catch
        {
            return 0;
        }
    }

    public void Drain()
    {
        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero)
            {
                try
                {
                    pa_simple_drain(_handle, out _);
                }
                catch
                {
                }
            }
        }
    }

    public void Flush()
    {
        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero)
            {
                try
                {
                    pa_simple_flush(_handle, out _);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Only raises the stop flag — the simple API is not thread-safe, so touching the handle here
    /// could race a <see cref="Write"/> in progress. The pump thread notices the flag, returns from
    /// its write within one buffer (~100 ms) and does the <see cref="Flush"/> itself.
    /// </summary>
    public void Stop() => _stopped = true;

    /// <summary>
    /// No-op: the simple API has no cork. The pump stops feeding instead, so at most one buffer of
    /// already-queued audio (tens of milliseconds) is still heard after a pause.
    /// </summary>
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
            if (_handle != IntPtr.Zero)
            {
                try
                {
                    pa_simple_free(_handle);
                }
                catch
                {
                }
                _handle = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        Close();
    }

    /// <summary>True when libpulse-simple can be loaded on this machine.</summary>
    internal static bool IsAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return NativeLibrary.TryLoad(LibPulseSimple, out var handle) && handle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lists PulseAudio sinks via <c>pactl</c>. libpulse's own introspection needs the async API and
    /// a mainloop, which is a lot of interop for a list that <c>pactl</c> already prints; when pactl
    /// is missing only the default-sink entry is offered, and playback still works.
    /// </summary>
    internal static IReadOnlyList<AudioDevice> GetDevices()
    {
        var devices = new List<AudioDevice>
        {
            new(string.Empty, "Default output", "PulseAudio") { IsDefault = true },
        };

        var defaultSink = GetDefaultSinkName();

        foreach (var line in RunLines("pactl", ["list", "short", "sinks"]))
        {
            // index<TAB>name<TAB>driver<TAB>sample-spec<TAB>state
            var columns = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2)
            {
                continue;
            }

            var name = columns[1].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            devices.Add(new AudioDevice(name, PrettifySinkName(name), "PulseAudio")
            {
                IsDefault = string.Equals(name, defaultSink, StringComparison.Ordinal),
            });
        }

        return devices;
    }

    private static string? GetDefaultSinkName()
    {
        foreach (var line in RunLines("pactl", ["info"]))
        {
            if (line.StartsWith("Default Sink:", StringComparison.OrdinalIgnoreCase))
            {
                return line["Default Sink:".Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>Turns "alsa_output.pci-0000_00_1f.3.analog-stereo" into something readable.</summary>
    internal static string PrettifySinkName(string name)
    {
        var text = name;
        if (text.StartsWith("alsa_output.", StringComparison.Ordinal))
        {
            text = text["alsa_output.".Length..];
        }

        text = text.Replace('_', ' ').Replace('-', ' ');
        return text.Length == 0 ? name : text;
    }

    private static IReadOnlyList<string> RunLines(string fileName, IReadOnlyList<string> arguments)
    {
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

            return process.ExitCode == 0
                ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
        }
        catch
        {
            return [];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttributes
    {
        public uint MaxLength;
        public uint TargetLength;
        public uint PreBuffer;
        public uint MinimumRequest;
        public uint FragmentSize;
    }

    [DllImport(LibPulseSimple, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pa_simple_new(
        string? server,
        string name,
        int direction,
        string? device,
        string streamName,
        ref SampleSpec sampleSpec,
        IntPtr channelMap,
        ref BufferAttributes bufferAttributes,
        out int error);

    [DllImport(LibPulseSimple, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pa_simple_write(IntPtr handle, byte[] data, nuint bytes, out int error);

    [DllImport(LibPulseSimple, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pa_simple_drain(IntPtr handle, out int error);

    [DllImport(LibPulseSimple, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pa_simple_flush(IntPtr handle, out int error);

    [DllImport(LibPulseSimple, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong pa_simple_get_latency(IntPtr handle, out int error);

    [DllImport(LibPulseSimple, CallingConvention = CallingConvention.Cdecl)]
    private static extern void pa_simple_free(IntPtr handle);
}
