namespace iTerminal.Core.AI.Voice;

/// <summary>
/// Dictation for the AI bar: while listening, the words the user speaks arrive as <see cref="TextRecognized"/>.
/// The service never blocks its caller — recognition runs on the engine's own threads, so the events
/// come from a worker thread and the subscriber must not touch the control tree from them.
/// </summary>
public interface IVoiceInputService
{
    /// <summary>True when this machine has a speech recognizer and a microphone to feed it.</summary>
    bool IsAvailable { get; }

    /// <summary>Why <see cref="IsAvailable"/> is false (or why the last start failed); empty otherwise.</summary>
    string UnavailableReason { get; }

    bool IsListening { get; }

    /// <summary>Starts listening. Returns false (and sets <see cref="UnavailableReason"/>) when it cannot.</summary>
    bool Start();

    void Stop();

    /// <summary>A phrase the recognizer is confident about.</summary>
    event EventHandler<string>? TextRecognized;

    /// <summary>Raised when <see cref="IsListening"/> changes, including when the engine stops by itself.</summary>
    event EventHandler? ListeningChanged;
}
