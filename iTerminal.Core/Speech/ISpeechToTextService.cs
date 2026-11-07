namespace iTerminal.Core.Speech;

/// <summary>
/// Voice input for the AI bar: record, transcribe, hand the text over. Every member returns at once; the work
/// runs on background tasks and the owner polls <see cref="State"/> and <see cref="TakeResult"/> from its idle pass.
/// </summary>
public interface ISpeechToTextService : IDisposable
{
    SpeechState State { get; }

    /// <summary>Raised from a background thread whenever <see cref="State"/> moves or a result arrives.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Starts a recording, or ends the running one and starts transcribing it. Does nothing while transcribing.
    /// Without the model it only says so in a notification.
    /// </summary>
    void Toggle();

    /// <summary>Abandons a recording or a transcription; nothing is returned for it.</summary>
    void Cancel();

    /// <summary>The recognised text of the last recording, once; null when there is none waiting.</summary>
    string? TakeResult();
}
