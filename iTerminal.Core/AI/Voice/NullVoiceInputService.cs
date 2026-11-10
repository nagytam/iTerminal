namespace iTerminal.Core.AI.Voice;

/// <summary>The voice input of a platform without a speech recognizer; the bar shows why instead of a mic.</summary>
public sealed class NullVoiceInputService(string reason = "voice input is only available on Windows") : IVoiceInputService
{
#pragma warning disable CS0067 // never raised: nothing here ever listens
    public event EventHandler<string>? TextRecognized;
    public event EventHandler? ListeningChanged;
#pragma warning restore CS0067

    public bool IsAvailable => false;

    public string UnavailableReason => reason;

    public bool IsListening => false;

    public bool Start() => false;

    public void Stop()
    {
    }
}
