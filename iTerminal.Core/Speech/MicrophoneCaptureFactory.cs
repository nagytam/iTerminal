using iConsole.Localization;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Speech;

/// <summary>
/// The recorder of this system: waveIn on Windows, <c>parecord</c> (then <c>arecord</c>) on Linux, and the ffmpeg
/// that is a runtime dependency anyway on macOS. Choosing finds programs on the PATH and opens nothing.
/// </summary>
public sealed class MicrophoneCaptureFactory : IMicrophoneCaptureFactory
{
    public IMicrophoneCapture Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WinMmMicrophoneCapture();
        }

        List<RecorderCommand> commands = OperatingSystem.IsMacOS() ? MacCommands() : LinuxCommands();
        if (commands.Count == 0)
        {
            throw new SpeechException(OperatingSystem.IsMacOS()
                ? UiText.Get("Voice.NoFfmpeg", "Voice input needs ffmpeg to record. Install it from Tools > Runtime Dependencies.")
                : UiText.Get("Voice.NoRecorder", "Voice input needs parecord (pulseaudio-utils) or arecord (alsa-utils) to record."));
        }

        return new ProcessMicrophoneCapture(commands);
    }

    internal static List<RecorderCommand> LinuxCommands() => LinuxCommands(GitRuntimeDependency.FindOnPath);

    internal static List<RecorderCommand> LinuxCommands(Func<string, string?> find)
    {
        List<RecorderCommand> commands = [];
        if (find("parecord") is { } parecord)
        {
            commands.Add(new RecorderCommand(parecord, ProcessMicrophoneCapture.ParecordArguments()));
        }

        if (find("arecord") is { } arecord)
        {
            commands.Add(new RecorderCommand(arecord, ProcessMicrophoneCapture.ArecordArguments()));
        }

        return commands;
    }

    internal static List<RecorderCommand> MacCommands() => MacCommands(program => GitRuntimeDependency.FindOnPath(program) ?? FindIn(program, "/opt/homebrew/bin", "/usr/local/bin"));

    internal static List<RecorderCommand> MacCommands(Func<string, string?> find)
    {
        if (find("ffmpeg") is not { } ffmpeg)
        {
            return [];
        }

        // ":default" is the default input where ffmpeg knows it; ":0" is the first audio device where it does not.
        return
        [
            new RecorderCommand(ffmpeg, ProcessMicrophoneCapture.FfmpegArguments(":default")),
            new RecorderCommand(ffmpeg, ProcessMicrophoneCapture.FfmpegArguments(":0")),
        ];
    }

    private static string? FindIn(string program, params string[] directories) =>
        directories.Select(directory => Path.Combine(directory, program)).FirstOrDefault(File.Exists);
}
