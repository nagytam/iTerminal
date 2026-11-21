namespace iConsole.Media.Speech;

/// <summary>Where the Whisper model file is, or null while it is not installed.</summary>
public interface IWhisperModelLocator
{
    string? ModelPath();
}
