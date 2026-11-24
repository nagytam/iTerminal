using iConsole.Localization;
using iConsole.Media.Speech;
using iTerminal.Core.Services.Evidence;
using System.Globalization;

namespace iTerminal.Core.Services.Voice;

/// <summary>What a recording left behind: the WAV file, its length, and the transcript when one was asked for and obtained.</summary>
/// <param name="WavPath">The saved recording.</param>
/// <param name="Duration">The length of the audio.</param>
/// <param name="Transcript">The recognised text, or null when transcription was off or failed.</param>
/// <param name="TranscriptPath">The plain-text transcript beside the WAV file, or null when there is none.</param>
/// <param name="Problem">Why transcription did not happen when it was asked for, or null.</param>
public sealed record VoiceRecording(string WavPath, TimeSpan Duration, string? Transcript, string? TranscriptPath, string? Problem);

/// <summary>
/// The recorder behind the voice recorder dialog: one microphone session at a time, saved as a WAV file and, when asked,
/// transcribed offline with the Whisper model. The methods block or do disk and model work, so the dialog calls them from
/// a background task.
/// </summary>
public interface IVoiceRecorderService : IDisposable
{
    /// <summary>True when the Whisper model is on this computer. Cheap.</summary>
    bool IsModelInstalled { get; }

    /// <summary>Opens the microphone and starts recording. Blocks briefly.</summary>
    /// <exception cref="iConsole.Media.Speech.SpeechException">There is no microphone, recorder or permission.</exception>
    void Start();

    /// <summary>
    /// Ends the recording, saves it, and transcribes it when <paramref name="transcribe"/> is set. Returns null when there was
    /// no recording or it was too short to keep.
    /// </summary>
    Task<VoiceRecording?> StopAsync(bool transcribe, CancellationToken cancellationToken);

    /// <summary>Ends the recording without keeping it.</summary>
    void Discard();
}

/// <summary>
/// Saves each recording as <c>voice-yyyyMMdd-HHmmss.wav</c> in <paramref name="folder"/>, so a recording is on disk before the
/// model runs and survives a failed transcription. The transcript goes beside it as a <c>.txt</c> file of the same name.
/// </summary>
public sealed class VoiceRecorderService(
    IMicrophoneCaptureFactory captures,
    ISpeechRecognizer recognizer,
    Func<string> folder) : IVoiceRecorderService
{
    private readonly object _gate = new();
    private IMicrophoneCapture? _capture;
    private bool _disposed;

    public bool IsModelInstalled => recognizer.IsModelInstalled;

    public void Start()
    {
        IMicrophoneCapture capture = captures.Create();
        try
        {
            capture.Start();
        }
        catch
        {
            capture.Dispose();
            throw;
        }

        IMicrophoneCapture? unused = null;
        lock (_gate)
        {
            if (_disposed || _capture is not null)
            {
                unused = capture;
            }
            else
            {
                _capture = capture;
            }
        }

        // A second Start, or a Start after Dispose, must not leave a microphone open.
        unused?.Dispose();
    }

    public async Task<VoiceRecording?> StopAsync(bool transcribe, CancellationToken cancellationToken)
    {
        IMicrophoneCapture? capture;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
        }

        if (capture is null)
        {
            return null;
        }

        byte[] pcm;
        try
        {
            pcm = await Task.Run(capture.Stop, CancellationToken.None);
        }
        finally
        {
            capture.Dispose();
        }

        if (pcm.Length < Pcm.MinBytes)
        {
            return null;
        }

        string directory = folder();
        Directory.CreateDirectory(directory);
        string wavPath = UniquePath(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), ".wav");
        await File.WriteAllBytesAsync(wavPath, WavFile.Build(pcm, Pcm.SampleRate), cancellationToken);
        TimeSpan duration = TimeSpan.FromSeconds(pcm.Length / (double)Pcm.BytesPerSecond);

        if (!transcribe)
        {
            return new VoiceRecording(wavPath, duration, null, null, null);
        }

        if (!recognizer.IsModelInstalled)
        {
            return new VoiceRecording(wavPath, duration, null, null, UiText.Get(
                "Voice.ModelMissing", "Voice input needs the Whisper model. Install it from Tools > Runtime Dependencies."));
        }

        try
        {
            string text = (await recognizer.TranscribeAsync(Pcm.ToSamples(pcm), cancellationToken)).Trim();
            string transcriptPath = Path.ChangeExtension(wavPath, ".txt");
            await File.WriteAllTextAsync(transcriptPath, text, cancellationToken);
            return new VoiceRecording(wavPath, duration, text, transcriptPath, null);
        }
        catch (Exception exception) when (exception is SpeechException or IOException)
        {
            // The recording is already on disk; only the transcript is missing.
            return new VoiceRecording(wavPath, duration, null, null, exception.Message);
        }
    }

    /// <summary>Releases the microphone in the background: a recorder process can take a moment to stop, and a key handler must not wait for it.</summary>
    public void Discard() => Release(wait: false);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        // The recorder is a child process on Linux and macOS: leave only once it is stopped.
        Release(wait: true);
    }

    private void Release(bool wait)
    {
        IMicrophoneCapture? capture;
        lock (_gate)
        {
            // Taken under the lock, so a Start that follows cannot be the capture this call disposes.
            capture = _capture;
            _capture = null;
        }

        if (capture is null)
        {
            return;
        }

        if (wait)
        {
            capture.Dispose();
        }
        else
        {
            Task.Run(capture.Dispose);
        }
    }

    /// <summary>The first free name of <c>voice-stamp.ext</c>, <c>voice-stamp-2.ext</c>, and so on, so one second never overwrites a recording.</summary>
    private static string UniquePath(string directory, string stamp, string extension)
    {
        string path = Path.Combine(directory, $"voice-{stamp}{extension}");
        int n = 2;
        while (File.Exists(path) || File.Exists(Path.ChangeExtension(path, ".txt")))
        {
            path = Path.Combine(directory, $"voice-{stamp}-{n}{extension}");
            n++;
        }

        return path;
    }
}
