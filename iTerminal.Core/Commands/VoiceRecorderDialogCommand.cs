using iConsole.Data;
using iTerminal.Core.Dialogs.Voice;

namespace iTerminal.Core.Commands;

public class VoiceRecorderDialogCommand(VoiceRecorderDialog voiceRecorderDialog) : ShowDialogCommand<VoiceRecorderDialog>(voiceRecorderDialog)
{
    public override string Title => "Voice recorder";
    public override string Description => "Record from the microphone to a WAV file, with an optional offline AI transcription (Whisper).";

    // No hot key: Ctrl+Shift+X is the password manager's alternate chord, every other Ctrl+Shift letter is bound (Ctrl+Shift+F
    // reaches the File menu's Ctrl+F mnemonic), and the function-key chords are taken or go to Quit (F10 ignores Alt).
    // Open it from Tools > Office or the command palette.
    public override Key? Key { get; init; } = null;
}
