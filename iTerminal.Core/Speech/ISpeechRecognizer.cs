namespace iTerminal.Core.Speech;

/// <summary>Turns a recording into text, offline.</summary>
public interface ISpeechRecognizer
{
    /// <summary>True when the speech model is on this computer. Cheap: it is asked from a key handler.</summary>
    bool IsModelInstalled { get; }

    /// <summary>Recognises 16 kHz mono samples in [-1, 1]. Slow (seconds): call it from a background task.</summary>
    /// <exception cref="SpeechException">The engine or the model could not be used.</exception>
    Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken);
}
