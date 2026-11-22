using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Media.Speech;
using iConsole.Services;
using iTerminal.Core.Services.Voice;
using System.Diagnostics;
using System.Globalization;

namespace iTerminal.Core.Dialogs.Voice;

/// <summary>
/// Opened from Tools > Office or the command palette (it has no hot key): records from the default microphone, saves the recording as a WAV file in the recordings folder, and, when the
/// AI transcription option is on (T), transcribes it offline with the Whisper model and saves the text beside it.
/// Enter starts and stops a recording; a recording stops by itself at <see cref="Pcm.MaxSeconds"/>. Esc discards a recording
/// that is going, cancels a transcription, and closes the dialog otherwise.
///
/// <para>Every step that touches the microphone, the disk or the model runs on a background task. The dialog keeps a snapshot under
/// a lock and <see cref="Rows"/> reads only that snapshot, so no key handler or repaint waits for the device.</para>
/// </summary>
public class VoiceRecorderDialog : Dialog
{
    private const int PreferredWidth = 78;
    private const int MaxTranscriptLines = 6;

    // Status, option and blank (3); the saved file and its length (2); blank, transcript heading, its lines and a cut mark
    // (3 + MaxTranscriptLines); a problem, blank and message (3); the footer and the frame (3).
    private const int PreferredHeight = 3 + 2 + 3 + MaxTranscriptLines + 3 + 3;

    private enum RecorderState
    {
        Idle,
        Starting,
        Recording,
        Working,
    }

    private readonly IVoiceRecorderService _recorder;
    private readonly object _gate = new();
    private RecorderState _state = RecorderState.Idle;
    private bool _transcribe;
    private bool _closed;
    private VoiceRecording? _last;
    private string? _message;
    private Stopwatch? _elapsed;
    private CancellationTokenSource? _cancellation;
    private Task _task = Task.CompletedTask;
    private long _revision;
    private long _paintedRevision = -1;
    private int _paintedSecond = -1;

    public VoiceRecorderDialog(IVoiceRecorderService recorder, IThemeService themeService)
        : base(nameof(VoiceRecorderDialog), themeService)
    {
        _recorder = recorder;
        Title = UiText.Get("Dialog.VoiceRecorder", "Voice recorder");
    }

    /// <summary>The background step the last key started. Nothing awaits it: it exists so a test can wait for the result instead of polling.</summary>
    internal Task RunTask => _task;

    /// <summary>Whether the AI transcription option is on.</summary>
    internal bool TranscriptionOn
    {
        get
        {
            lock (_gate)
            {
                return _transcribe;
            }
        }
    }

    /// <summary>The state of the recorder right now, as the snapshot holds it.</summary>
    internal bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _state == RecorderState.Recording;
            }
        }
    }

    /// <summary>The last recording saved by this dialog, or null.</summary>
    internal VoiceRecording? LastRecording
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            // The height is fixed when the dialog opens, so it leaves room for the longest content the dialog can show
            // (a full transcript), not for the rows that exist at that moment.
            int width = (int)Math.Max(20, Math.Min(PreferredWidth, (long)Parent.ViewSize.Width));
            int height = (int)Math.Max(10, Math.Min(PreferredHeight, (long)Parent.ViewSize.Height));
            CenterInParent(new Size(width, height));
        }

        base.OnInitializeLayout();
    }

    /// <summary>Enter: starts a recording when none is going, and stops the one that is.</summary>
    public void Toggle()
    {
        RecorderState state;
        lock (_gate)
        {
            state = _state;
        }

        switch (state)
        {
            case RecorderState.Idle:
                StartRecording();
                break;
            case RecorderState.Recording:
                StopRecording();
                break;
        }
    }

    /// <summary>T: switches the AI transcription option. It is only offered while the Whisper model is installed.</summary>
    internal void ToggleTranscription()
    {
        lock (_gate)
        {
            if (_state != RecorderState.Idle)
            {
                return;
            }

            if (!_recorder.IsModelInstalled)
            {
                _message = UiText.Get("Voice.ModelMissing", "Voice input needs the Whisper model. Install it from Tools > Runtime Dependencies.");
            }
            else
            {
                _transcribe = !_transcribe;
                _message = null;
            }

            Changed();
        }
    }

    private void StartRecording()
    {
        lock (_gate)
        {
            if (_state != RecorderState.Idle)
            {
                return;
            }

            _state = RecorderState.Starting;
            _message = null;
            _last = null;
            Changed();
        }

        _task = Task.Run(() =>
        {
            try
            {
                _recorder.Start();
            }
            catch (SpeechException exception)
            {
                lock (_gate)
                {
                    _state = RecorderState.Idle;
                    _message = exception.Message;
                    Changed();
                }

                return;
            }

            bool closed;
            lock (_gate)
            {
                closed = _closed;
                if (!closed)
                {
                    _state = RecorderState.Recording;
                    _elapsed = Stopwatch.StartNew();
                    Changed();
                }
            }

            // The dialog closed while the microphone was opening: nobody will stop this recording.
            if (closed)
            {
                _recorder.Discard();
            }
        });
    }

    private void StopRecording()
    {
        bool transcribe;
        CancellationToken token;
        lock (_gate)
        {
            if (_state != RecorderState.Recording)
            {
                return;
            }

            _state = RecorderState.Working;
            _elapsed?.Stop();
            _message = null;
            transcribe = _transcribe;
            _cancellation = new CancellationTokenSource();
            token = _cancellation.Token;
            Changed();
        }

        _task = Task.Run(async () =>
        {
            VoiceRecording? recording = null;
            string? message = null;
            try
            {
                recording = await _recorder.StopAsync(transcribe, token);
                if (recording is null)
                {
                    message = UiText.Get("Voice.Recorder.TooShort", "The recording was too short to keep.");
                }
            }
            catch (OperationCanceledException)
            {
                message = UiText.Get("Voice.Recorder.Cancelled", "Cancelled.");
            }
            catch (SpeechException exception)
            {
                // The recorder failed while it was running (a revoked permission, a device that went away): say why.
                message = exception.Message;
            }
            catch (IOException exception)
            {
                message = exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                message = exception.Message;
            }
            finally
            {
                lock (_gate)
                {
                    _state = RecorderState.Idle;
                    _last = recording;
                    _message = message;
                    _cancellation?.Dispose();
                    _cancellation = null;
                    Changed();
                }
            }
        });
    }

    /// <summary>Esc during a recording: the audio is dropped and nothing is saved.</summary>
    private void DiscardRecording()
    {
        lock (_gate)
        {
            if (_state != RecorderState.Recording)
            {
                return;
            }

            _state = RecorderState.Idle;
            _elapsed?.Stop();
            _last = null;
            _message = UiText.Get("Voice.Recorder.Discarded", "Recording discarded.");
            Changed();
        }

        _recorder.Discard();
    }

    /// <summary>Esc during a transcription: the transcription is cancelled; a recording already saved stays on disk.</summary>
    private void CancelWork()
    {
        lock (_gate)
        {
            if (_state == RecorderState.Working)
            {
                _cancellation?.Cancel();
            }
        }
    }

    /// <summary>Marks the snapshot as changed, so the next idle pass repaints. Call under the lock.</summary>
    private void Changed() => _revision++;

    public override void OnIdle()
    {
        base.OnIdle();

        bool stop = false;
        int second = -1;
        long revision;
        lock (_gate)
        {
            if (_state == RecorderState.Recording && _elapsed is not null)
            {
                second = (int)_elapsed.Elapsed.TotalSeconds;
                stop = _elapsed.Elapsed.TotalSeconds >= Pcm.MaxSeconds;
            }

            revision = _revision;
        }

        // The recording's time limit: it ends here as if Enter had been pressed.
        if (stop)
        {
            StopRecording();
            return;
        }

        if (revision != _paintedRevision || second != _paintedSecond)
        {
            _paintedRevision = revision;
            _paintedSecond = second;
            StateHasChanged();
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (plain && keyEvent.ConsoleKey == ConsoleKey.Enter)
        {
            Toggle();
            StateHasChanged();
            return true;
        }

        if (plain && keyEvent.ConsoleKey == ConsoleKey.T)
        {
            ToggleTranscription();
            StateHasChanged();
            return true;
        }

        if (plain && keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            RecorderState state;
            lock (_gate)
            {
                state = _state;
            }

            // Esc acts on the work first; the next Esc, with nothing going on, closes the dialog.
            if (state == RecorderState.Recording)
            {
                DiscardRecording();
                StateHasChanged();
                return true;
            }

            if (state == RecorderState.Working)
            {
                CancelWork();
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Close()
    {
        bool discard;
        lock (_gate)
        {
            _closed = true;
            discard = _state == RecorderState.Recording;
            if (_state == RecorderState.Working)
            {
                _cancellation?.Cancel();
            }
        }

        if (discard)
        {
            _recorder.Discard();
        }

        base.Close();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        RecorderState state;
        lock (_gate)
        {
            state = _state;
        }

        switch (state)
        {
            case RecorderState.Recording:
                yield return ("Enter", UiText.Get("Voice.Recorder.Hint.Stop", "Stop and save"));
                yield return ("Esc", UiText.Get("Voice.Recorder.Hint.Discard", "Discard the recording"));
                yield break;
            case RecorderState.Working:
                yield return ("Esc", UiText.Get("Voice.Recorder.Hint.Cancel", "Cancel"));
                yield break;
            case RecorderState.Starting:
                yield return ("Esc", UiText.Get("Common.Close", "Close"));
                yield break;
        }

        yield return ("Enter", UiText.Get("Voice.Recorder.Hint.Start", "Start recording"));
        yield return ("T", UiText.Get("Voice.Recorder.Hint.Transcription", "Switch the AI transcription"));
        yield return ("Esc", UiText.Get("Common.Close", "Close"));
    }

    /// <summary>
    /// The rows of the dialog, built from the snapshot. The status, the option, the last recording and its transcript; a
    /// transcript longer than the dialog is cut after <see cref="MaxTranscriptLines"/> lines.
    /// </summary>
    internal List<(string Text, Color Color)> Rows(int innerWidth, ColorScheme scheme)
    {
        RecorderState state;
        VoiceRecording? last;
        string? message;
        bool transcribe;
        TimeSpan elapsed;
        lock (_gate)
        {
            state = _state;
            last = _last;
            message = _message;
            transcribe = _transcribe;
            elapsed = _elapsed?.Elapsed ?? TimeSpan.Zero;
        }

        bool model = _recorder.IsModelInstalled;
        List<(string Text, Color Color)> rows =
        [
            (StatusText(state, elapsed), state is RecorderState.Recording ? Color.DarkYellow : scheme.Foreground),
            (OptionText(transcribe, model), model ? scheme.Foreground : scheme.DisabledForeground),
            (string.Empty, scheme.Foreground),
        ];

        if (last is not null)
        {
            rows.Add((string.Format(CultureInfo.CurrentCulture, UiText.Get("Voice.Recorder.Saved", "Saved: {0}"), last.WavPath), scheme.Foreground));
            rows.Add((string.Format(CultureInfo.CurrentCulture, UiText.Get("Voice.Recorder.Length", "Length: {0}"), Clock(last.Duration)), scheme.Foreground));
            if (last.Transcript is not null)
            {
                rows.Add((string.Empty, scheme.Foreground));
                rows.Add((UiText.Get("Voice.Recorder.Transcript", "Transcript"), scheme.Foreground));
                List<string> lines = Wrap(last.Transcript, Math.Max(1, innerWidth - 2));
                foreach (string line in lines.Take(MaxTranscriptLines))
                {
                    rows.Add(("  " + line, scheme.Foreground));
                }

                if (lines.Count > MaxTranscriptLines)
                {
                    rows.Add(("  ...", scheme.DisabledForeground));
                }
            }

            if (last.Problem is not null)
            {
                rows.Add((last.Problem, Color.DarkRed));
            }
        }

        if (message is not null)
        {
            rows.Add((string.Empty, scheme.Foreground));
            rows.Add((message, Color.DarkRed));
        }

        return rows.Select(row => (row.Text.Length > innerWidth ? row.Text.Cut(innerWidth) : row.Text, row.Color)).ToList();
    }

    private string OptionText(bool transcribe, bool model)
    {
        string value = transcribe
            ? UiText.Get("Voice.Recorder.TranscriptionOn", "on")
            : UiText.Get("Voice.Recorder.TranscriptionOff", "off");
        string text = string.Format(CultureInfo.CurrentCulture, UiText.Get("Voice.Recorder.Transcription", "AI transcription: {0}"), value);
        return model ? text : $"{text} ({UiText.Get("Voice.Recorder.NoModel", "model not installed")})";
    }

    private static string StatusText(RecorderState state, TimeSpan elapsed) => state switch
    {
        RecorderState.Starting => UiText.Get("Voice.Recorder.Starting", "Opening the microphone..."),
        RecorderState.Recording => string.Format(CultureInfo.CurrentCulture, UiText.Get("Voice.Recorder.Recording", "Recording {0}."), Clock(elapsed)),
        RecorderState.Working => UiText.Get("Voice.Recorder.Saving", "Saving..."),
        _ => UiText.Get("Voice.Recorder.Idle", "Enter starts recording. It stops by itself after 60 seconds."),
    };

    private static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";

    /// <summary>Breaks a text at spaces into lines no wider than <paramref name="width"/>; a word longer than that is cut.</summary>
    internal static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];
        string line = string.Empty;
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string piece = word;
            while (piece.Length > width)
            {
                if (line.Length > 0)
                {
                    lines.Add(line);
                    line = string.Empty;
                }

                lines.Add(piece[..width]);
                piece = piece[width..];
            }

            if (line.Length == 0)
            {
                line = piece;
            }
            else if (line.Length + 1 + piece.Length <= width)
            {
                line += " " + piece;
            }
            else
            {
                lines.Add(line);
                line = piece;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        List<(string Text, Color Color)> rows = Rows(innerWidth, colorScheme);
        // Stops above the footer row, so a short screen cuts the list and never the footer.
        for (int i = 0; i < rows.Count && i + 1 < ViewSize.Height - 2; i++)
        {
            WriteRow(buffer, colorScheme, innerWidth, i + 1, rows[i].Text, rows[i].Color);
        }

        string footer = FooterText();
        WriteRow(buffer, colorScheme, innerWidth, (int)ViewSize.Height - 2, footer.Length <= innerWidth ? footer : footer.Cut(innerWidth), colorScheme.DisabledForeground);
    }

    private string FooterText()
    {
        RecorderState state;
        lock (_gate)
        {
            state = _state;
        }

        return state switch
        {
            RecorderState.Recording => UiText.Get("Voice.Recorder.Footer.Recording", "Enter:stop and save  Esc:discard"),
            RecorderState.Working => UiText.Get("Voice.Recorder.Footer.Working", "Esc:cancel"),
            RecorderState.Starting => UiText.Get("Voice.Recorder.Footer.Starting", "Esc:close"),
            _ => UiText.Get("Voice.Recorder.Footer.Idle", "Enter:record  T:AI transcription  Esc:close"),
        };
    }

    private void WriteRow(IBuffer buffer, ColorScheme colorScheme, int innerWidth, int row, string text, Color color)
    {
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + row),
            new Size(innerWidth, 1),
            new ConxelString(text.ExpandRight(innerWidth), color, colorScheme.Background));
    }
}
