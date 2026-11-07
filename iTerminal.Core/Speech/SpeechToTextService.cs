using iConsole.Localization;
using iConsole.Services;
using Serilog;

namespace iTerminal.Core.Speech;

/// <summary>
/// Record, then transcribe, one session at a time. A session is one background task: it opens the recorder, waits
/// for the second <see cref="Toggle"/> (or the time limit), stops, and recognises. A generation number makes a session
/// that was cancelled unable to publish anything, whenever it finishes.
/// </summary>
public sealed class SpeechToTextService : ISpeechToTextService
{
    private readonly IMicrophoneCaptureFactory _captures;
    private readonly ISpeechRecognizer _recognizer;
    private readonly INotificationService? _notifications;
    private readonly ILogger? _logger;
    private readonly TimeSpan _maxRecording;
    private readonly object _gate = new();

    private SpeechState _state = SpeechState.Idle;
    private int _generation;
    private CancellationTokenSource? _session;
    private TaskCompletionSource? _stop;
    private Task _task = Task.CompletedTask;
    private string? _result;
    private bool _disposed;

    public SpeechToTextService(
        IMicrophoneCaptureFactory captures,
        ISpeechRecognizer recognizer,
        INotificationService? notifications = null,
        ILogger? logger = null)
        : this(captures, recognizer, notifications, logger, TimeSpan.FromSeconds(Pcm.MaxSeconds))
    {
    }

    internal SpeechToTextService(
        IMicrophoneCaptureFactory captures,
        ISpeechRecognizer recognizer,
        INotificationService? notifications,
        ILogger? logger,
        TimeSpan maxRecording)
    {
        _captures = captures;
        _recognizer = recognizer;
        _notifications = notifications;
        _logger = logger;
        _maxRecording = maxRecording;
    }

    public event EventHandler? Changed;

    public SpeechState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public void Toggle()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            switch (_state)
            {
                case SpeechState.Recording:
                    _stop?.TrySetResult();
                    return;

                case SpeechState.Transcribing:
                    return;
            }

            if (!_recognizer.IsModelInstalled)
            {
                Tell(UiText.Get("Voice.ModelMissing", "Voice input needs the Whisper model. Install it from Tools > Runtime Dependencies."));
                return;
            }

            _generation++;
            _state = SpeechState.Recording;
            _result = null;
            // The previous session was cancelled or has finished: nobody uses its source any more.
            _session?.Dispose();
            _session = new CancellationTokenSource();
            _stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int generation = _generation;
            CancellationToken token = _session.Token;
            TaskCompletionSource stop = _stop;
            // A cancelled session may still be releasing the microphone: the new one waits for it.
            Task previous = _task;
            _task = Task.Run(() => RunAsync(previous, generation, stop, token));
        }

        RaiseChanged();
    }

    public void Cancel()
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != SpeechState.Idle;
            _generation++;
            _state = SpeechState.Idle;
            _result = null;
            _session?.Cancel();
            _stop?.TrySetResult();
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    public string? TakeResult()
    {
        lock (_gate)
        {
            string? result = _result;
            _result = null;
            return result;
        }
    }

    public void Dispose()
    {
        Task task;
        lock (_gate)
        {
            _disposed = true;
            task = _task;
        }

        Cancel();

        // The recorder is a child process on Linux and macOS: leave only once it is stopped, or it outlives the application
        // with the microphone open. Bounded, because a shutdown does not wait for ever.
        try
        {
            task.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
    }

    private async Task RunAsync(Task previous, int generation, TaskCompletionSource stop, CancellationToken token)
    {
        IMicrophoneCapture? capture = null;
        try
        {
            await previous;
            token.ThrowIfCancellationRequested();
            capture = _captures.Create();
            capture.Start();

            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(_maxRecording);
                try
                {
                    await stop.Task.WaitAsync(limit.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // The time limit: what was recorded so far is transcribed.
                }
            }

            token.ThrowIfCancellationRequested();
            byte[] pcm = capture.Stop();
            capture.Dispose();
            capture = null;

            if (!Move(generation, SpeechState.Transcribing))
            {
                return;
            }

            if (pcm.Length < Pcm.MinBytes)
            {
                Finish(generation, null);
                return;
            }

            float[] samples = Pcm.ToSamples(pcm);
            if (!samples.Any(sample => sample != 0f))
            {
                throw new SpeechException(UiText.Get(
                    "Voice.Silence",
                    "The microphone delivered silence. Check that it is not muted and that this program may use it."));
            }

            string text = await _recognizer.TranscribeAsync(samples, token);
            Finish(generation, text);
        }
        catch (OperationCanceledException)
        {
            Finish(generation, null);
        }
        catch (SpeechException exception)
        {
            Fail(generation, exception.Message);
        }
        catch (Exception exception)
        {
            _logger?.Warning(exception, "Voice input failed");
            Fail(generation, exception.Message);
        }
        finally
        {
            capture?.Dispose();
        }
    }

    /// <summary>Moves the state of the session, unless it was cancelled meanwhile.</summary>
    private bool Move(int generation, SpeechState state)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return false;
            }

            _state = state;
        }

        RaiseChanged();
        return true;
    }

    private void Finish(int generation, string? text)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            _state = SpeechState.Idle;
            _result = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        RaiseChanged();
    }

    private void Fail(int generation, string message)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            _state = SpeechState.Idle;
            _result = null;
        }

        Tell(message);
        RaiseChanged();
    }

    private void Tell(string message) =>
        _notifications?.Notify(UiText.Get("Voice.Title", "Voice input"), message, NotificationSeverity.Warning);

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
