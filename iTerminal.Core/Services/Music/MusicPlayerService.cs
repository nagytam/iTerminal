using iTerminal.Core.Audio;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// The playback engine. A single background pump thread owns the decoder and the sink and does all
/// the blocking work; public methods only record a request and wake the pump, which is what keeps
/// the UI responsive — a key handler never waits for a file to open or a device to accept audio.
///
/// <para>The sink's blocking <see cref="IAudioSink.Write"/> is the clock: the pump naturally runs at
/// playback speed with no timer. To interrupt a write that is waiting on the device (stop, track
/// change), callers use <see cref="IAudioSink.Stop"/>, the one sink member that is safe to call
/// from another thread.</para>
///
/// <para>Advancing to the next track happens inside the pump rather than in a UI callback, so a
/// playlist keeps playing whether or not the player dialog is open.</para>
/// </summary>
public sealed class MusicPlayerService : IMusicPlayerService
{
    private const int PumpIdlePollMilliseconds = 150;
    private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private readonly IAudioDeviceService _deviceService;
    private readonly Serilog.ILogger? _logger;

    private readonly object _requestLock = new();
    private readonly AutoResetEvent _wakeUp = new(false);

    private Thread? _pumpThread;
    private volatile bool _shutdown;
    private volatile IAudioSink? _currentSink;

    private int? _requestedIndex;
    private TimeSpan? _requestedStartPosition;
    private TimeSpan? _requestedSeek;
    private bool _requestedStop;
    private volatile bool _requestedPause;

    private volatile PlaybackState _state = PlaybackState.Stopped;
    private long _positionTicks;
    private long _durationTicks;
    private volatile int _volume = 70;
    private volatile string _statusMessage = string.Empty;
    private volatile bool _statusIsError;
    private string? _deviceId;
    private RepeatMode _repeatMode = RepeatMode.Off;
    private int _consecutiveFailures;

    public MusicPlayerService(IAudioDeviceService deviceService, Serilog.ILogger? logger = null)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _logger = logger;
    }

    public Playlist Playlist { get; } = new();

    public PlaybackState State => _state;

    public MusicTrack? CurrentTrack => Playlist.CurrentTrack;

    public TimeSpan Position => new(Interlocked.Read(ref _positionTicks));

    public TimeSpan Duration => new(Interlocked.Read(ref _durationTicks));

    public int Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_volume != clamped)
            {
                _volume = clamped;
                RaiseStateChanged();
            }
        }
    }

    public RepeatMode RepeatMode
    {
        get
        {
            lock (_requestLock)
            {
                return _repeatMode;
            }
        }
        set
        {
            lock (_requestLock)
            {
                _repeatMode = value;
            }
            RaiseStateChanged();
        }
    }

    public bool IsShuffled
    {
        get => Playlist.IsShuffled;
        set
        {
            Playlist.IsShuffled = value;
            RaiseStateChanged();
        }
    }

    public string? DeviceId
    {
        get
        {
            lock (_requestLock)
            {
                return _deviceId;
            }
        }
    }

    public string StatusMessage => _statusMessage;

    public bool StatusIsError => _statusIsError;

    public event EventHandler? StateChanged;

    /// <summary>
    /// Swaps the queue, cues the given track, and announces both at once. Playback is left alone:
    /// this exists for restoring a saved queue at start-up, where nothing is loaded yet — a cued
    /// track is one the panel marks and the header names, not one that starts playing by itself.
    /// </summary>
    public void ReplacePlaylist(IEnumerable<MusicTrack> tracks, int currentIndex = -1)
    {
        Playlist.ReplaceAll(tracks);
        Playlist.CurrentIndex = currentIndex;
        RaiseStateChanged();
    }

    public void PlayIndex(int index)
    {
        if (index < 0 || index >= Playlist.Count)
        {
            Stop();
            return;
        }

        // The sink to interrupt is the one playing *now*, taken before the request is posted. Read
        // after it, it could already be the sink the pump has just opened for this very request —
        // and stopping that one ended the new track at once with "The audio device stopped
        // accepting data." (the pump starts fast enough on a first play to win that race).
        var interrupted = _currentSink;
        lock (_requestLock)
        {
            _requestedIndex = index;
            _requestedStartPosition = null;
            _requestedStop = false;
            _requestedPause = false;
            _consecutiveFailures = 0;
        }

        // Starting playback by hand is the point at which a stale message stops being interesting.
        // Automatic advances deliberately leave it alone — see PlayOne.
        SetStatus(string.Empty, isError: false);
        EnsurePumpStarted();
        // Unblocks a write that is currently waiting on the device so the new track starts now.
        interrupted?.Stop();
        _wakeUp.Set();
    }

    public void TogglePlayPause()
    {
        switch (_state)
        {
            case PlaybackState.Playing:
                Pause();
                break;

            case PlaybackState.Paused:
                Resume();
                break;

            default:
                var index = Playlist.CurrentIndex;
                PlayIndex(index >= 0 ? index : 0);
                break;
        }
    }

    public void Pause()
    {
        if (_state != PlaybackState.Playing)
        {
            return;
        }

        _requestedPause = true;
        _wakeUp.Set();
    }

    public void Resume()
    {
        if (_state != PlaybackState.Paused)
        {
            return;
        }

        _requestedPause = false;
        _wakeUp.Set();
    }

    public void Stop()
    {
        lock (_requestLock)
        {
            _requestedStop = true;
            _requestedIndex = null;
            _requestedPause = false;
        }

        _currentSink?.Stop();
        _wakeUp.Set();
    }

    public void Next()
    {
        var next = Playlist.GetNextIndex(RepeatMode == RepeatMode.One ? RepeatMode.All : RepeatMode);
        if (next < 0)
        {
            Stop();
            return;
        }

        PlayIndex(next);
    }

    public void Previous()
    {
        if (_state != PlaybackState.Stopped && Position > RestartThreshold)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        var previous = Playlist.GetPreviousIndex(RepeatMode == RepeatMode.One ? RepeatMode.All : RepeatMode);
        if (previous < 0)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        PlayIndex(previous);
    }

    public void Seek(TimeSpan position)
    {
        if (_state == PlaybackState.Stopped)
        {
            return;
        }

        lock (_requestLock)
        {
            _requestedSeek = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }

        _wakeUp.Set();
    }

    public void SeekRelative(TimeSpan offset)
    {
        if (_state == PlaybackState.Stopped)
        {
            return;
        }

        var target = Position + offset;
        var duration = Duration;
        if (duration > TimeSpan.Zero && target > duration)
        {
            target = duration;
        }

        Seek(target < TimeSpan.Zero ? TimeSpan.Zero : target);
    }

    public void SetDevice(string? deviceId)
    {
        lock (_requestLock)
        {
            _deviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        }

        // Reopening is the only way to move a stream between endpoints; resume where we left off so
        // switching devices mid-song is barely noticeable.
        if (_state is PlaybackState.Playing or PlaybackState.Paused)
        {
            var index = Playlist.CurrentIndex;
            var resumeAt = Position;
            if (index >= 0)
            {
                // Taken before the request, for the reason PlayIndex gives.
                var interrupted = _currentSink;
                lock (_requestLock)
                {
                    _requestedIndex = index;
                    _requestedStartPosition = resumeAt;
                    _requestedStop = false;
                    _requestedPause = false;
                }

                EnsurePumpStarted();
                interrupted?.Stop();
                _wakeUp.Set();
                return;
            }
        }

        RaiseStateChanged();
    }

    /// <summary>The pump thread once it exists, so a test can assert how it was configured.</summary>
    internal Thread? PumpThreadForTest => _pumpThread;

    private void EnsurePumpStarted()
    {
        if (_pumpThread is not null || _shutdown)
        {
            return;
        }

        lock (_requestLock)
        {
            if (_pumpThread is not null || _shutdown)
            {
                return;
            }

            _pumpThread = new Thread(Pump)
            {
                IsBackground = true,
                Name = "iTerminal audio pump",

                // Audio is the one thing in this app with a hard deadline: the sink's buffer drains
                // in real time whether or not this thread gets scheduled, and arriving late is not a
                // slow repaint, it is an audible gap. The thread spends nearly all its life blocked
                // inside the sink's write, so raising it above the render and task-manager threads
                // costs them nothing — it only decides who wins when the device finally asks for
                // more samples.
                Priority = ThreadPriority.AboveNormal,
            };
            _pumpThread.Start();
        }
    }

    private void Pump()
    {
        while (!_shutdown)
        {
            int index;
            TimeSpan startPosition;

            lock (_requestLock)
            {
                _requestedStop = false;
                if (_requestedIndex is null)
                {
                    index = -1;
                    startPosition = TimeSpan.Zero;
                }
                else
                {
                    index = _requestedIndex.Value;
                    startPosition = _requestedStartPosition ?? TimeSpan.Zero;
                    _requestedIndex = null;
                    _requestedStartPosition = null;
                }
            }

            if (index < 0)
            {
                if (_state != PlaybackState.Stopped)
                {
                    SetState(PlaybackState.Stopped);
                }

                _wakeUp.WaitOne(PumpIdlePollMilliseconds);
                continue;
            }

            PlayOne(index, startPosition);
        }
    }

    private void PlayOne(int index, TimeSpan startPosition)
    {
        var track = Playlist[index];
        if (track is null)
        {
            SetState(PlaybackState.Stopped);
            return;
        }

        Playlist.CurrentIndex = index;
        Interlocked.Exchange(ref _positionTicks, startPosition.Ticks);
        Interlocked.Exchange(ref _durationTicks, track.Duration.Ticks);

        // Which track is cued has just changed, and nothing below will say so: the only other
        // notification on this path is SetState(Playing), which is a no-op when a track ends and the
        // next one starts because the state was already Playing. Without this the playlist panel
        // keeps the highlight — and the header — on the track that finished.
        RaiseStateChanged();

        IAudioDecoder? decoder = null;
        IAudioSink? sink = null;

        try
        {
            decoder = AudioDecoderFactory.Create(track.Path);

            Interlocked.Exchange(ref _durationTicks, decoder.Duration.Ticks);
            if (decoder.Duration > TimeSpan.Zero && track.Duration != decoder.Duration)
            {
                // First real play is when the duration becomes known; write it back so the playlist
                // stops showing "--:--" for this row.
                Playlist.UpdateAt(index, track with { Duration = decoder.Duration });
            }

            sink = _deviceService.CreateSink();
            if (sink is null)
            {
                SetStatus("No audio output backend is available on this system.", isError: true);
                SetState(PlaybackState.Stopped);
                return;
            }

            sink.Open(decoder.Format, DeviceId);
            _currentSink = sink;

            if (startPosition > TimeSpan.Zero)
            {
                decoder.TrySeek(startPosition);
            }

            // The status is intentionally left as-is. When the pump skips past an unplayable file
            // and lands here, the explanation for the skip has to survive long enough to be read —
            // clearing it on the next successful track would make the error flash and vanish.
            SetState(PlaybackState.Playing);
            _consecutiveFailures = 0;

            var reachedEnd = RunPlaybackLoop(decoder, sink);

            if (reachedEnd && !_shutdown)
            {
                AdvanceAfterTrackEnd();
            }
        }
        catch (Exception exception)
        {
            _logger?.Warning(exception, "Music playback failed for {Path}", track.Path);
            SetStatus($"{track.FileName}: {exception.Message}", isError: true);
            HandleTrackFailure();
        }
        finally
        {
            _currentSink = null;

            try
            {
                sink?.Stop();
                sink?.Dispose();
            }
            catch
            {
            }

            try
            {
                decoder?.Dispose();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Feeds the sink until the track ends or a request interrupts it.
    /// Returns true only when the decoder reached its natural end.
    /// </summary>
    private bool RunPlaybackLoop(IAudioDecoder decoder, IAudioSink sink)
    {
        var format = decoder.Format;
        var buffer = new byte[Math.Max(format.BlockAlign, format.BytesPerSecond / 10)];

        while (!_shutdown)
        {
            lock (_requestLock)
            {
                if (_requestedStop || _requestedIndex is not null)
                {
                    return false;
                }
            }

            if (_requestedPause)
            {
                if (!WaitWhilePaused(sink))
                {
                    return false;
                }
            }

            TimeSpan? seek;
            lock (_requestLock)
            {
                seek = _requestedSeek;
                _requestedSeek = null;
            }

            if (seek is TimeSpan seekTarget)
            {
                if (decoder.TrySeek(seekTarget))
                {
                    // Drop what is already queued, otherwise the old audio plays before the new
                    // position is heard.
                    sink.Flush();
                    Interlocked.Exchange(ref _positionTicks, seekTarget.Ticks);
                }
                else
                {
                    SetStatus("This format cannot be seeked.", isError: false);
                }
            }

            int read;
            try
            {
                read = decoder.Read(buffer);
            }
            catch (Exception exception)
            {
                _logger?.Warning(exception, "Decoder read failed");
                read = 0;
            }

            if (read <= 0)
            {
                sink.Drain();
                return true;
            }

            ApplyVolume(buffer.AsSpan(0, read), _volume);

            var written = sink.Write(buffer.AsSpan(0, read));
            if (written <= 0)
            {
                // The sink refused the data: either it was stopped from another thread (a new
                // request is pending and will be picked up above) or the device went away.
                lock (_requestLock)
                {
                    if (!_requestedStop && _requestedIndex is null)
                    {
                        SetStatus("The audio device stopped accepting data.", isError: true);
                        return false;
                    }
                }

                return false;
            }

            var buffered = sink.BufferedDuration;
            var position = decoder.Position - buffered;
            Interlocked.Exchange(ref _positionTicks, position > TimeSpan.Zero ? position.Ticks : 0);
        }

        return false;
    }

    /// <summary>Parks the pump while paused. Returns false when the wait ended for another reason.</summary>
    private bool WaitWhilePaused(IAudioSink sink)
    {
        sink.Pause();
        SetState(PlaybackState.Paused);

        while (_requestedPause && !_shutdown)
        {
            lock (_requestLock)
            {
                if (_requestedStop || _requestedIndex is not null)
                {
                    return false;
                }
            }

            _wakeUp.WaitOne(PumpIdlePollMilliseconds);
        }

        if (_shutdown)
        {
            return false;
        }

        sink.Resume();
        SetState(PlaybackState.Playing);
        return true;
    }

    private void AdvanceAfterTrackEnd()
    {
        var next = Playlist.GetNextIndex(RepeatMode);
        if (next < 0)
        {
            Playlist.CurrentIndex = -1;
            Interlocked.Exchange(ref _positionTicks, 0);
            SetState(PlaybackState.Stopped);
            return;
        }

        lock (_requestLock)
        {
            _requestedIndex ??= next;
        }
    }

    /// <summary>
    /// Skips past a track that would not open. Bounded by the playlist length so a folder full of
    /// unplayable files fails once and stops, rather than spinning through them forever.
    /// </summary>
    private void HandleTrackFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= Math.Max(1, Playlist.Count))
        {
            _consecutiveFailures = 0;
            SetState(PlaybackState.Stopped);
            return;
        }

        var next = Playlist.GetNextIndex(RepeatMode == RepeatMode.One ? RepeatMode.Off : RepeatMode);
        if (next < 0)
        {
            SetState(PlaybackState.Stopped);
            return;
        }

        lock (_requestLock)
        {
            _requestedIndex ??= next;
        }
    }

    /// <summary>
    /// Scales samples in place. The curve is quadratic rather than linear because loudness is
    /// perceived roughly logarithmically — a linear slider spends most of its travel sounding loud.
    /// </summary>
    internal static void ApplyVolume(Span<byte> pcm, int volume)
    {
        if (volume >= 100)
        {
            return;
        }

        if (volume <= 0)
        {
            pcm.Clear();
            return;
        }

        var gain = volume / 100.0;
        gain *= gain;

        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(samples[i] * gain);
        }
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        RaiseStateChanged();
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessage = message ?? string.Empty;
        _statusIsError = isError;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A misbehaving subscriber must never take down the pump thread.
        }
    }

    public void Dispose()
    {
        _shutdown = true;
        _requestedPause = false;

        try
        {
            _currentSink?.Stop();
        }
        catch
        {
        }

        _wakeUp.Set();

        var pumpThread = _pumpThread;
        _pumpThread = null;
        try
        {
            pumpThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _wakeUp.Dispose();
    }
}
