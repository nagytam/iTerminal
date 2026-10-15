using iTerminal.Core.Audio;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// An <see cref="IAudioSink"/> that records what it was given instead of touching hardware, so the
/// engine can be driven end to end in a unit test. <see cref="WriteDelay"/> emulates the pacing a
/// real device provides — without it the pump would consume a whole track in microseconds and
/// transport states would never be observable.
/// </summary>
internal sealed class FakeAudioSink : IAudioSink
{
    private readonly object _syncLock = new();
    private readonly List<byte> _written = [];
    private volatile bool _stopped;

    public PcmFormat OpenedFormat { get; private set; }

    public string? OpenedDeviceId { get; private set; }

    public int OpenCount { get; private set; }

    public int FlushCount { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Per-write pause, standing in for the time a device takes to play the audio.</summary>
    public TimeSpan WriteDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan BufferedDuration => TimeSpan.Zero;

    public long WrittenByteCount
    {
        get
        {
            lock (_syncLock)
            {
                return _written.Count;
            }
        }
    }

    public byte[] WrittenBytes
    {
        get
        {
            lock (_syncLock)
            {
                return [.. _written];
            }
        }
    }

    public void Open(PcmFormat format, string? deviceId)
    {
        lock (_syncLock)
        {
            OpenedFormat = format;
            OpenedDeviceId = deviceId;
            OpenCount++;
            _stopped = false;
            IsPaused = false;
        }
    }

    public int Write(ReadOnlySpan<byte> pcm)
    {
        if (_stopped)
        {
            return 0;
        }

        lock (_syncLock)
        {
            _written.AddRange(pcm.ToArray());
        }

        if (WriteDelay > TimeSpan.Zero)
        {
            Thread.Sleep(WriteDelay);
        }

        return pcm.Length;
    }

    public void Drain()
    {
    }

    public void Flush()
    {
        lock (_syncLock)
        {
            FlushCount++;
        }
    }

    public void Stop() => _stopped = true;

    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;

    public void Dispose() => IsDisposed = true;
}

/// <summary>Hands out <see cref="FakeAudioSink"/>s and a fixed device list.</summary>
internal sealed class FakeAudioDeviceService : IAudioDeviceService
{
    private readonly List<FakeAudioSink> _createdSinks = [];

    public FakeAudioDeviceService(bool hasOutput = true)
    {
        HasAudioOutput = hasOutput;
    }

    public string ActiveBackendName => HasAudioOutput ? "Fake" : string.Empty;

    public bool HasAudioOutput { get; }

    public int RefreshCount { get; private set; }

    /// <summary>Every sink handed to the engine, oldest first — one per track start.</summary>
    public IReadOnlyList<FakeAudioSink> CreatedSinks
    {
        get
        {
            lock (_createdSinks)
            {
                return [.. _createdSinks];
            }
        }
    }

    public FakeAudioSink? LastSink
    {
        get
        {
            lock (_createdSinks)
            {
                return _createdSinks.Count > 0 ? _createdSinks[^1] : null;
            }
        }
    }

    /// <summary>Applied to each new sink, so a test can slow playback down to observable speed.</summary>
    public TimeSpan WriteDelay { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<AudioDevice> GetDevices() =>
        HasAudioOutput
            ?
            [
                new AudioDevice("", "Default output", "Fake") { IsDefault = true },
                new AudioDevice("1", "Speakers", "Fake"),
                new AudioDevice("2", "Headphones", "Fake"),
            ]
            : [];

    public void Refresh() => RefreshCount++;

    public IAudioSink? CreateSink()
    {
        if (!HasAudioOutput)
        {
            return null;
        }

        var sink = new FakeAudioSink { WriteDelay = WriteDelay };
        lock (_createdSinks)
        {
            _createdSinks.Add(sink);
        }

        return sink;
    }
}
