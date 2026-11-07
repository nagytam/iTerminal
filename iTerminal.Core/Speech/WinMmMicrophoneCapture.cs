using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using iConsole.Localization;

namespace iTerminal.Core.Speech;

/// <summary>
/// Windows recording through waveIn in <c>winmm.dll</c>, the counterpart of <c>WinMmAudioSink</c>: the driver signals an
/// event as each buffer fills and a dedicated thread (no managed callback) copies the full ones out and hands them back.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WinMmMicrophoneCapture : IMicrophoneCapture
{
    private const int BufferCount = 4;
    private const int BufferMilliseconds = 100;
    private const int CallbackEvent = 0x00050000;
    private const int WaveMapper = -1;
    private const int WhdrDone = 0x00000001;
    private const int WhdrPrepared = 0x00000002;

    private readonly PcmBuffer _buffer = new();
    private readonly object _gate = new();
    private IntPtr _handle;
    private IntPtr[] _headers = [];
    private IntPtr[] _data = [];
    private AutoResetEvent? _signal;
    private Thread? _reader;
    private volatile bool _running;

    public void Start()
    {
        lock (_gate)
        {
            if (waveInGetNumDevs() == 0)
            {
                throw new SpeechException(UiText.Get("Voice.NoMicrophone", "No microphone was found."));
            }

            _signal = new AutoResetEvent(false);
            var format = new WaveFormatEx
            {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSecond = Pcm.SampleRate,
                AverageBytesPerSecond = Pcm.BytesPerSecond,
                BlockAlign = 2,
                BitsPerSample = 16,
                Size = 0,
            };

            int result = waveInOpen(out _handle, WaveMapper, ref format, _signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
            if (result != 0)
            {
                ReleaseSignal();
                throw new SpeechException(UiText.Get("Voice.MicOpenFailed", "The microphone could not be opened") + $" (waveInOpen {result}).");
            }

            int bytes = Pcm.BytesPerSecond * BufferMilliseconds / 1000;
            _headers = new IntPtr[BufferCount];
            _data = new IntPtr[BufferCount];
            int headerSize = Marshal.SizeOf<WaveHeader>();
            int failure = 0;
            for (int i = 0; i < BufferCount && failure == 0; i++)
            {
                _data[i] = Marshal.AllocHGlobal(bytes);
                _headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = _data[i], BufferLength = (uint)bytes }, _headers[i], fDeleteOld: false);
                failure = waveInPrepareHeader(_handle, _headers[i], headerSize);
                if (failure == 0)
                {
                    failure = waveInAddBuffer(_handle, _headers[i], headerSize);
                }
            }

            if (failure == 0)
            {
                failure = waveInStart(_handle);
            }

            if (failure != 0)
            {
                // Whatever was set up is given back, so a failed start does not keep the device open.
                Release();
                throw new SpeechException(UiText.Get("Voice.MicOpenFailed", "The microphone could not be opened") + $" (waveIn {failure}).");
            }

            _running = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Microphone" };
            _reader.Start();
        }
    }

    public byte[] Stop()
    {
        Thread? reader;
        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return _buffer.ToArray();
            }

            _running = false;
            waveInStop(_handle);
            // Reset returns every queued buffer, the partly filled one included, marked done.
            waveInReset(_handle);
            _signal?.Set();
            reader = _reader;
        }

        reader?.Join(TimeSpan.FromSeconds(2));
        lock (_gate)
        {
            Harvest(requeue: false);
            Release();
        }

        return _buffer.ToArray();
    }

    public void Dispose()
    {
        _ = Stop();
    }

    private void ReadLoop()
    {
        while (_running)
        {
            _signal?.WaitOne(BufferMilliseconds * 2);
            lock (_gate)
            {
                if (_handle == IntPtr.Zero || !_running)
                {
                    return;
                }

                Harvest(requeue: true);
            }
        }
    }

    /// <summary>Copies out every buffer the driver has filled; gives it back to the driver when <paramref name="requeue"/>.</summary>
    private void Harvest(bool requeue)
    {
        int headerSize = Marshal.SizeOf<WaveHeader>();
        byte[] scratch = new byte[Pcm.BytesPerSecond * BufferMilliseconds / 1000];
        for (int i = 0; i < _headers.Length; i++)
        {
            var header = Marshal.PtrToStructure<WaveHeader>(_headers[i]);
            if ((header.Flags & WhdrDone) == 0)
            {
                continue;
            }

            int recorded = (int)Math.Min(header.BytesRecorded, (uint)scratch.Length);
            if (recorded > 0)
            {
                Marshal.Copy(header.Data, scratch, 0, recorded);
                _buffer.Append(scratch.AsSpan(0, recorded));
            }

            waveInUnprepareHeader(_handle, _headers[i], headerSize);
            header.Flags = 0;
            header.BytesRecorded = 0;
            Marshal.StructureToPtr(header, _headers[i], fDeleteOld: false);
            if (requeue)
            {
                waveInPrepareHeader(_handle, _headers[i], headerSize);
                waveInAddBuffer(_handle, _headers[i], headerSize);
            }
        }
    }

    private void Release()
    {
        int headerSize = Marshal.SizeOf<WaveHeader>();
        for (int i = 0; i < _headers.Length; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                var header = Marshal.PtrToStructure<WaveHeader>(_headers[i]);
                if ((header.Flags & WhdrPrepared) != 0)
                {
                    waveInUnprepareHeader(_handle, _headers[i], headerSize);
                }

                Marshal.FreeHGlobal(_headers[i]);
            }

            if (_data[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_data[i]);
            }
        }

        _headers = [];
        _data = [];
        waveInClose(_handle);
        _handle = IntPtr.Zero;
        ReleaseSignal();
    }

    private void ReleaseSignal()
    {
        _signal?.Dispose();
        _signal = null;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WaveFormatEx
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
    internal struct WaveHeader
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

    [DllImport("winmm.dll")]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr handle);
}
