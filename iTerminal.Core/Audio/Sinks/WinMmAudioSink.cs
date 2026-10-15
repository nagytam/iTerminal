using iTerminal.Core.Data.Music;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace iTerminal.Core.Audio.Sinks;

/// <summary>
/// Windows output through the waveOut API in <c>winmm.dll</c>. waveOut is chosen over WASAPI
/// deliberately: it ships with every Windows version, needs no COM interop or NuGet, exposes the
/// per-endpoint device IDs the selector needs, and its double-buffered write model maps exactly
/// onto the pump's blocking <see cref="Write"/> contract.
///
/// <para>Buffers are handed to the driver in a small ring; the driver signals a manual-reset event
/// as each one completes, which is what makes <see cref="Write"/> block for exactly as long as the
/// device is busy.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinMmAudioSink : IAudioSink
{
    private const int BufferCount = 4;
    private const int BufferMilliseconds = 80;

    private const int CallbackEvent = 0x00050000;
    private const int WaveMapper = -1;
    private const int WhdrDone = 0x00000001;
    private const int WhdrPrepared = 0x00000002;

    private readonly object _syncLock = new();

    private IntPtr _handle;
    private IntPtr[] _headerPointers = [];
    private IntPtr[] _dataPointers = [];
    private byte[] _copyScratch = [];
    private AutoResetEvent? _bufferDone;
    private PcmFormat _format;
    private int _nextBuffer;
    private volatile bool _stopped;
    private bool _paused;

    public TimeSpan BufferedDuration
    {
        get
        {
            lock (_syncLock)
            {
                if (_handle == IntPtr.Zero || !_format.IsValid)
                {
                    return TimeSpan.Zero;
                }

                long queued = 0;
                for (int i = 0; i < _headerPointers.Length; i++)
                {
                    var header = Marshal.PtrToStructure<WaveHeader>(_headerPointers[i]);
                    if ((header.Flags & WhdrDone) == 0 && (header.Flags & WhdrPrepared) != 0)
                    {
                        queued += header.BufferLength;
                    }
                }

                return _format.BytesToTime(queued);
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
            _paused = false;
            _nextBuffer = 0;
            _bufferDone = new AutoResetEvent(false);

            var waveFormat = new WaveFormatEx
            {
                FormatTag = 1, // WAVE_FORMAT_PCM
                Channels = (short)format.Channels,
                SamplesPerSecond = format.SampleRate,
                AverageBytesPerSecond = format.BytesPerSecond,
                BlockAlign = (short)format.BlockAlign,
                BitsPerSample = PcmFormat.BitsPerSample,
                Size = 0,
            };

            var deviceIndex = ParseDeviceId(deviceId);
            var result = waveOutOpen(
                out _handle,
                deviceIndex,
                ref waveFormat,
                _bufferDone.SafeWaitHandle.DangerousGetHandle(),
                IntPtr.Zero,
                CallbackEvent);

            if (result != 0)
            {
                _bufferDone.Dispose();
                _bufferDone = null;
                throw new InvalidOperationException($"Could not open audio device (waveOutOpen returned {result}).");
            }

            var bufferBytes = Math.Max(format.BlockAlign, format.BytesPerSecond * BufferMilliseconds / 1000);
            bufferBytes -= bufferBytes % format.BlockAlign;

            _headerPointers = new IntPtr[BufferCount];
            _dataPointers = new IntPtr[BufferCount];
            _copyScratch = new byte[bufferBytes];
            for (int i = 0; i < BufferCount; i++)
            {
                _dataPointers[i] = Marshal.AllocHGlobal(bufferBytes);
                _headerPointers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());

                var header = new WaveHeader
                {
                    Data = _dataPointers[i],
                    BufferLength = (uint)bufferBytes,
                    // Marked done so the first pass through the ring sees every buffer as free.
                    Flags = WhdrDone,
                };
                Marshal.StructureToPtr(header, _headerPointers[i], fDeleteOld: false);
            }
        }
    }

    public int Write(ReadOnlySpan<byte> pcm)
    {
        var written = 0;
        while (written < pcm.Length)
        {
            if (_stopped)
            {
                break;
            }

            var slot = WaitForFreeBuffer();
            if (slot < 0)
            {
                break;
            }

            int chunk;
            lock (_syncLock)
            {
                if (_handle == IntPtr.Zero)
                {
                    break;
                }

                var headerPointer = _headerPointers[slot];
                var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);

                var capacity = (int)header.BufferLength;
                chunk = Math.Min(capacity, pcm.Length - written);
                chunk -= chunk % _format.BlockAlign;
                if (chunk <= 0)
                {
                    // Fewer bytes left than one frame: nothing more can be queued this call.
                    break;
                }

                if ((header.Flags & WhdrPrepared) != 0)
                {
                    waveOutUnprepareHeader(_handle, headerPointer, Marshal.SizeOf<WaveHeader>());
                }

                // Staged through a reusable array: Marshal.Copy has no Span overload, and allocating
                // per write would churn the heap dozens of times a second during playback.
                pcm.Slice(written, chunk).CopyTo(_copyScratch);
                Marshal.Copy(_copyScratch, 0, header.Data, chunk);

                header.BufferLength = (uint)chunk;
                header.Flags = 0;
                header.BytesRecorded = 0;
                header.Loops = 0;
                Marshal.StructureToPtr(header, headerPointer, fDeleteOld: false);

                if (waveOutPrepareHeader(_handle, headerPointer, Marshal.SizeOf<WaveHeader>()) != 0
                    || waveOutWrite(_handle, headerPointer, Marshal.SizeOf<WaveHeader>()) != 0)
                {
                    break;
                }

                _nextBuffer = (slot + 1) % BufferCount;
            }

            written += chunk;
        }

        return written;
    }

    /// <summary>
    /// Returns the index of a buffer the driver has finished with, waiting on the completion event
    /// when all of them are still in flight. Returns -1 when the sink was stopped or closed.
    /// </summary>
    private int WaitForFreeBuffer()
    {
        while (!_stopped)
        {
            AutoResetEvent? bufferDone;
            lock (_syncLock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return -1;
                }

                for (int i = 0; i < BufferCount; i++)
                {
                    var slot = (_nextBuffer + i) % BufferCount;
                    var header = Marshal.PtrToStructure<WaveHeader>(_headerPointers[slot]);
                    if ((header.Flags & WhdrDone) != 0)
                    {
                        return slot;
                    }
                }

                bufferDone = _bufferDone;
            }

            if (bufferDone is null)
            {
                return -1;
            }

            // Time-boxed so a lost completion signal degrades into a small stall rather than a hang.
            bufferDone.WaitOne(TimeSpan.FromMilliseconds(BufferMilliseconds * 2));
        }

        return -1;
    }

    public void Drain()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline && !_stopped)
        {
            var pending = false;
            lock (_syncLock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return;
                }

                for (int i = 0; i < BufferCount; i++)
                {
                    var header = Marshal.PtrToStructure<WaveHeader>(_headerPointers[i]);
                    if ((header.Flags & WhdrDone) == 0 && (header.Flags & WhdrPrepared) != 0)
                    {
                        pending = true;
                        break;
                    }
                }
            }

            if (!pending)
            {
                return;
            }

            Thread.Sleep(10);
        }
    }

    public void Flush()
    {
        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero)
            {
                // waveOutReset marks every queued buffer done, which also releases a blocked write.
                waveOutReset(_handle);
                _paused = false;
            }
        }
    }

    public void Stop()
    {
        _stopped = true;
        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero)
            {
                waveOutReset(_handle);
            }
        }
        _bufferDone?.Set();
    }

    public void Pause()
    {
        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero && !_paused)
            {
                waveOutPause(_handle);
                _paused = true;
            }
        }
    }

    public void Resume()
    {
        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero && _paused)
            {
                waveOutRestart(_handle);
                _paused = false;
            }
        }
    }

    private void Close()
    {
        Stop();

        lock (_syncLock)
        {
            if (_handle != IntPtr.Zero)
            {
                for (int i = 0; i < _headerPointers.Length; i++)
                {
                    if (_headerPointers[i] == IntPtr.Zero)
                    {
                        continue;
                    }

                    var header = Marshal.PtrToStructure<WaveHeader>(_headerPointers[i]);
                    if ((header.Flags & WhdrPrepared) != 0)
                    {
                        waveOutUnprepareHeader(_handle, _headerPointers[i], Marshal.SizeOf<WaveHeader>());
                    }
                }

                waveOutClose(_handle);
                _handle = IntPtr.Zero;
            }

            foreach (var pointer in _headerPointers)
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            foreach (var pointer in _dataPointers)
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            _headerPointers = [];
            _dataPointers = [];

            _bufferDone?.Dispose();
            _bufferDone = null;
        }
    }

    public void Dispose() => Close();

    private static int ParseDeviceId(string? deviceId) =>
        int.TryParse(deviceId, out var index) && index >= 0 ? index : WaveMapper;

    /// <summary>
    /// Enumerates waveOut endpoints. The WAVE_MAPPER pseudo-device is listed first as the default,
    /// so the player has a sensible target before the user picks anything.
    /// </summary>
    internal static IReadOnlyList<AudioDevice> GetDevices()
    {
        var devices = new List<AudioDevice>
        {
            new(WaveMapper.ToString(), "Default output", "WinMM") { IsDefault = true },
        };

        try
        {
            var count = waveOutGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                var caps = new WaveOutCaps();
                if (waveOutGetDevCapsW((IntPtr)i, ref caps, Marshal.SizeOf<WaveOutCaps>()) != 0)
                {
                    continue;
                }

                var name = caps.ProductName?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = $"Output device {i}";
                }

                devices.Add(new AudioDevice(i.ToString(), name, "WinMM"));
            }
        }
        catch
        {
            // Enumeration is best-effort; the default device entry alone still allows playback.
        }

        return devices;
    }

    internal static bool IsAvailable()
    {
        try
        {
            return waveOutGetNumDevs() > 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSecond;
        public int AverageBytesPerSecond;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveOutCaps
    {
        public short ManufacturerId;
        public short ProductId;
        public int DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;
        public int Formats;
        public short Channels;
        public short Reserved;
        public int Support;
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static extern int waveOutOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static extern int waveOutReset(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "waveOutPause")]
    private static extern int waveOutPause(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "waveOutRestart")]
    private static extern int waveOutRestart(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static extern int waveOutClose(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "waveOutGetNumDevs")]
    private static extern int waveOutGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "waveOutGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern int waveOutGetDevCapsW(IntPtr deviceId, ref WaveOutCaps caps, int size);
}
