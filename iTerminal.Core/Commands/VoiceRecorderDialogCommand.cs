using iConsole.Data;
using iTerminal.Core.Dialogs.Voice;

namespace iTerminal.Core.Commands;

public class VoiceRecorderDialogCommand(VoiceRecorderDialog voiceRecorderDialog) : ShowDialogCommand<VoiceRecorderDialog>(voiceRecorderDialog)
{
    public override string Title => "Voice recorder";
    public override string Description => "Record from the microphone to a WAV file, with an optional offline AI transcription (Whisper).";

    // Ctrl+Shift+X. Key constructor order is (consoleKey, shift, alt, control). Ctrl+Shift+P is the command palette, and the
    // Ctrl+Shift+C and Ctrl+Shift+V chords are the terminal's copy and paste.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.X, true, false, true);
}
