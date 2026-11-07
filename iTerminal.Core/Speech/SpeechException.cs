namespace iTerminal.Core.Speech;

/// <summary>
/// Voice input cannot work, and the message says why in words for the user (already localised): no microphone,
/// no recorder program, no permission, an engine that will not load.
/// </summary>
public sealed class SpeechException(string message) : Exception(message);
