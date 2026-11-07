namespace iTerminal.Core.Speech;

/// <summary>What voice input is doing right now.</summary>
public enum SpeechState
{
    Idle,
    Recording,
    Transcribing,
}
