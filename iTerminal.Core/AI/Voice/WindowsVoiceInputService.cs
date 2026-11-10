using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Recognition;

namespace iTerminal.Core.AI.Voice;

/// <summary>
/// Dictation through the speech recognizer Windows ships (System.Speech) on the default microphone.
/// The engine is created on the first start and kept: building it takes a second or more, which no
/// key handler may spend, so <see cref="Start"/> hands that work to a background task.
/// </summary>
public sealed class WindowsVoiceInputService : IVoiceInputService, IDisposable
{
    /// <summary>Phrases the recognizer is less sure of than this are dropped rather than typed.</summary>
    private const float MinimumConfidence = 0.4f;

    private readonly Lock _gate = new();
    private object? _engine;
    private int _state; // 0 idle, 1 starting, 2 listening
    private string _reason = string.Empty;

    public event EventHandler<string>? TextRecognized;
    public event EventHandler? ListeningChanged;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string UnavailableReason => _reason;

    public bool IsListening => Volatile.Read(ref _state) == 2;

    public bool Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            _reason = "voice input is only available on Windows";
            return false;
        }

        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return true;
        }

        _reason = string.Empty;
        _ = Task.Run(StartEngine);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private void StartEngine()
    {
        try
        {
            lock (_gate)
            {
                if (_engine is not SpeechRecognitionEngine engine)
                {
                    engine = new SpeechRecognitionEngine(CultureInfo.CurrentUICulture is { } culture
                        && SpeechRecognitionEngine.InstalledRecognizers().Any(r => r.Culture.Name == culture.Name)
                            ? culture
                            : SpeechRecognitionEngine.InstalledRecognizers().First().Culture);
                    engine.LoadGrammar(new DictationGrammar());
                    engine.SpeechRecognized += OnSpeechRecognized;
                    engine.SetInputToDefaultAudioDevice();
                    _engine = engine;
                }

                engine.RecognizeAsync(RecognizeMode.Multiple);
            }

            Volatile.Write(ref _state, 2);
        }
        catch (Exception ex)
        {
            // No recognizer installed, no microphone, or access to it denied.
            _reason = ex is InvalidOperationException or InvalidCastException or ArgumentException
                ? "no speech recognizer or microphone found"
                : ex.Message;
            Volatile.Write(ref _state, 0);
        }

        ListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    [SupportedOSPlatform("windows")]
    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence >= MinimumConfidence && !string.IsNullOrWhiteSpace(e.Result.Text))
        {
            TextRecognized?.Invoke(this, e.Result.Text);
        }
    }

    public void Stop()
    {
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref _state, 0) == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                lock (_gate)
                {
                    (_engine as SpeechRecognitionEngine)?.RecognizeAsyncCancel();
                }
            }
            catch (Exception)
            {
                // Already stopped.
            }
        });
        ListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Stop();
            lock (_gate)
            {
                (_engine as SpeechRecognitionEngine)?.Dispose();
                _engine = null;
            }
        }
    }
}
