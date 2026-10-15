using iTerminal.Core.Audio;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Chooses the usable audio backend for this machine and answers "what can I play through?" for the
/// device selector. Availability probing costs a native library load or a child process, so results
/// are cached until <see cref="Refresh"/> is called (which the player does when the user reopens
/// the selector, so hot-plugged devices show up without a restart).
///
/// <para>Implementations never throw: with no backend at all, <see cref="ActiveBackendName"/> is
/// empty and <see cref="GetDevices"/> returns nothing, and the player reports that instead of
/// failing.</para>
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>Name of the selected backend ("WinMM", "PulseAudio", "ALSA"), or empty when none.</summary>
    string ActiveBackendName { get; }

    /// <summary>Whether any backend is usable — false means this machine has no audio output.</summary>
    bool HasAudioOutput { get; }

    /// <summary>Output endpoints of the active backend, default first.</summary>
    IReadOnlyList<AudioDevice> GetDevices();

    /// <summary>Re-probes backends and re-reads the device list.</summary>
    void Refresh();

    /// <summary>Creates an unopened sink on the active backend, or null when there is no backend.</summary>
    IAudioSink? CreateSink();
}
