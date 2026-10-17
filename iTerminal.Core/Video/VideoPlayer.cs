using System.Diagnostics;

namespace iTerminal.Core.Video;

public enum VideoPlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>One decoded picture. Immutable once published, so the UI can hold it while the next one is made.</summary>
public sealed record VideoFrame(byte[] Rgb, int Columns, int Rows, TimeSpan Time);

/// <summary>
/// Plays one video: a single background thread reads frames from an <see cref="IVideoDecoder"/>,
/// holds each until its presentation time and publishes it as <see cref="Frame"/>. The UI never
/// waits for any of this — the transport methods only record a request and wake the thread, which
/// keeps key handlers well inside the 200 ms budget, and drawing just reads the latest frame.
///
/// <para>The clock is the player's own stopwatch rather than audio (the viewer is silent), so a
/// frame is shown when <see cref="Position"/> reaches its timestamp. After a seek or a resize the
/// clock is re-based on the first frame that arrives, because starting ffmpeg takes a moment and
/// that moment must not be played back as a burst of late frames.</para>
///
/// <para>Seeking, resizing and stopping all reopen the decoder. While paused or stopped the thread
/// reads exactly one frame after a reopen, so the picture follows the request.</para>
/// </summary>
public sealed class VideoPlayer : IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(50);

    private readonly IVideoDecoder _decoder;
    private readonly object _lock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly double _frameRate;

    private VideoPlaybackState _state = VideoPlaybackState.Stopped;
    private bool _disposed;
    private bool _started;

    // The request the thread has yet to act on.
    private bool _reopenPending;
    private TimeSpan _reopenAt;

    // The size the frames are made at.
    private int _columns;
    private int _rows;

    // The clock: position = base + time since the clock started, while it runs.
    private TimeSpan _clockBase;
    private long _clockStart;
    private bool _clockRunning;

    private VideoFrame? _frame;
    private long _frameVersion;
    private string _error = string.Empty;

    public VideoPlayer(IVideoDecoder decoder)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _frameRate = decoder.Info.FrameRate > 0 ? decoder.Info.FrameRate : 25;
        _thread = new Thread(Run) { IsBackground = true, Name = "VideoPlayer" };
    }

    public VideoInfo Info => _decoder.Info;

    public TimeSpan Duration => _decoder.Info.Duration;

    public VideoPlaybackState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>The latest picture, or null before the first one has been decoded.</summary>
    public VideoFrame? Frame => Volatile.Read(ref _frame);

    /// <summary>Moves with every published frame, so the UI can tell whether there is anything new to draw.</summary>
    public long FrameVersion => Interlocked.Read(ref _frameVersion);

    /// <summary>Why nothing could be shown, or empty.</summary>
    public string Error
    {
        get
        {
            lock (_lock)
            {
                return _error;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_lock)
            {
                return PositionLocked();
            }
        }
    }

    private TimeSpan PositionLocked()
    {
        var position = _clockBase;
        if (_clockRunning)
        {
            position += Stopwatch.GetElapsedTime(_clockStart);
        }

        var duration = Duration;
        return duration > TimeSpan.Zero && position > duration ? duration : position;
    }

    /// <summary>Starts the thread and shows the first picture at the given size. Call once.</summary>
    public void Start(int columns, int rows)
    {
        lock (_lock)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            _columns = Math.Max(1, columns);
            _rows = Math.Max(1, rows);
            _reopenPending = true;
            _reopenAt = TimeSpan.Zero;
        }

        _thread.Start();
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_disposed || _state == VideoPlaybackState.Playing)
            {
                return;
            }

            // Playing a finished or stopped video starts it over.
            if (_state == VideoPlaybackState.Stopped && PositionLocked() > TimeSpan.Zero)
            {
                RequestReopenLocked(TimeSpan.Zero);
            }

            _state = VideoPlaybackState.Playing;
            StartClockLocked();
        }

        _wake.Set();
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_state != VideoPlaybackState.Playing)
            {
                return;
            }

            _clockBase = PositionLocked();
            _clockRunning = false;
            _state = VideoPlaybackState.Paused;
        }

        _wake.Set();
    }

    public void TogglePlayPause()
    {
        if (State == VideoPlaybackState.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Back to the first picture and stopped.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _state = VideoPlaybackState.Stopped;
            _clockRunning = false;
            _clockBase = TimeSpan.Zero;
            RequestReopenLocked(TimeSpan.Zero);
        }

        _wake.Set();
    }

    public void SeekTo(TimeSpan target)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            target = ClampLocked(target);
            _clockBase = target;
            if (_clockRunning)
            {
                _clockStart = Stopwatch.GetTimestamp();
            }

            RequestReopenLocked(target);
        }

        _wake.Set();
    }

    public void SeekBy(TimeSpan offset) => SeekTo(Position + offset);

    /// <summary>Changes the size frames are made at. A no-op when it did not change.</summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        lock (_lock)
        {
            if (_disposed || (columns == _columns && rows == _rows))
            {
                return;
            }

            _columns = columns;
            _rows = rows;
            if (_started)
            {
                RequestReopenLocked(PositionLocked());
            }
        }

        _wake.Set();
    }

    private TimeSpan ClampLocked(TimeSpan target)
    {
        if (target < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // A little short of the end, so a seek there still has a frame to show.
        var duration = Duration;
        var last = duration - TimeSpan.FromSeconds(1.0 / _frameRate * 2);
        return duration > TimeSpan.Zero && target > last ? (last > TimeSpan.Zero ? last : TimeSpan.Zero) : target;
    }

    private void StartClockLocked()
    {
        _clockStart = Stopwatch.GetTimestamp();
        _clockRunning = true;
    }

    private void RequestReopenLocked(TimeSpan offset)
    {
        _reopenPending = true;
        _reopenAt = offset;
        _error = string.Empty;
    }

    #region The thread

    private void Run()
    {
        try
        {
            RunLoop();
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            // Disposed under the loop: the wait handle or the decoder went first. Nothing to do.
        }
        catch (Exception exception)
        {
            // A worker thread has no one to throw to; an unhandled exception here would end the app.
            Fail(exception.Message);
        }
    }

    private void RunLoop()
    {
        byte[] buffer = [];
        var haveStream = false;
        var wantPreview = false;
        var streamOffset = TimeSpan.Zero;
        long streamFrames = 0;
        var rebase = false;

        while (!IsDisposed)
        {
            bool reopen;
            TimeSpan offset;
            int columns;
            int rows;
            lock (_lock)
            {
                reopen = _reopenPending;
                offset = _reopenAt;
                columns = _columns;
                rows = _rows;
                _reopenPending = false;
            }

            if (reopen)
            {
                try
                {
                    _decoder.Open(offset, columns, rows);
                }
                catch (Exception exception)
                {
                    if (IsDisposed)
                    {
                        break;
                    }

                    Fail(exception.Message);
                    haveStream = false;
                    wantPreview = false;
                    continue;
                }

                buffer = new byte[columns * rows * 3];
                haveStream = true;
                wantPreview = true;
                rebase = true;
                streamOffset = offset;
                streamFrames = 0;
            }

            if (!haveStream || (State != VideoPlaybackState.Playing && !wantPreview))
            {
                _wake.WaitOne();
                continue;
            }

            if (!_decoder.ReadFrame(buffer))
            {
                if (IsDisposed)
                {
                    break;
                }

                if (HasPendingRequest)
                {
                    continue;
                }

                haveStream = false;
                wantPreview = false;
                OnEndOfStream(streamOffset, streamFrames);
                continue;
            }

            var time = streamOffset + TimeSpan.FromSeconds(streamFrames / _frameRate);
            streamFrames++;

            if (rebase)
            {
                // The first picture after a (re)open: from here the clock runs from its timestamp.
                RebaseClock(time);
                rebase = false;
            }
            else if (!wantPreview && !WaitUntilDue(time))
            {
                continue;
            }

            Publish(new VideoFrame((byte[])buffer.Clone(), columns, rows, time));
            wantPreview = false;
        }
    }

    private bool IsDisposed
    {
        get
        {
            lock (_lock)
            {
                return _disposed;
            }
        }
    }

    private bool HasPendingRequest
    {
        get
        {
            lock (_lock)
            {
                return _reopenPending;
            }
        }
    }

    private void RebaseClock(TimeSpan time)
    {
        lock (_lock)
        {
            if (_reopenPending)
            {
                return;
            }

            _clockBase = time;
            if (_clockRunning)
            {
                _clockStart = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>Holds the frame until the clock reaches it. False when a request or a dispose made it moot.</summary>
    private bool WaitUntilDue(TimeSpan time)
    {
        while (true)
        {
            TimeSpan? remaining;
            lock (_lock)
            {
                if (_disposed || _reopenPending)
                {
                    return false;
                }

                if (_state == VideoPlaybackState.Playing)
                {
                    var wait = time - PositionLocked();
                    if (wait <= TimeSpan.Zero)
                    {
                        return true;
                    }

                    remaining = wait < MaxWait ? wait : MaxWait;
                }
                else if (_state == VideoPlaybackState.Stopped)
                {
                    return false;
                }
                else
                {
                    // Paused with a frame in hand: keep it until play, a seek or a dispose.
                    remaining = null;
                }
            }

            if (remaining is null)
            {
                _wake.WaitOne();
            }
            else
            {
                _wake.WaitOne(remaining.Value);
            }
        }
    }

    private void Publish(VideoFrame frame)
    {
        Volatile.Write(ref _frame, frame);
        Interlocked.Increment(ref _frameVersion);
    }

    /// <summary>
    /// The decoder ran dry. A first frame that never came means the file cannot be shown; a stream
    /// that played to its end goes back to a stopped first picture.
    /// </summary>
    private void OnEndOfStream(TimeSpan offset, long framesRead)
    {
        lock (_lock)
        {
            if (framesRead == 0 && offset == TimeSpan.Zero)
            {
                _error = "No picture could be decoded from this file.";
                _state = VideoPlaybackState.Stopped;
                _clockRunning = false;
                _clockBase = TimeSpan.Zero;
                return;
            }

            _state = VideoPlaybackState.Stopped;
            _clockRunning = false;
            _clockBase = TimeSpan.Zero;
            RequestReopenLocked(TimeSpan.Zero);
        }
    }

    private void Fail(string message)
    {
        lock (_lock)
        {
            _error = message;
            _state = VideoPlaybackState.Stopped;
            _clockRunning = false;
        }
    }

    #endregion

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _wake.Set();
        // Ends the read the thread may be blocked in.
        _decoder.Dispose();

        if (_started && Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _wake.Dispose();
    }
}
