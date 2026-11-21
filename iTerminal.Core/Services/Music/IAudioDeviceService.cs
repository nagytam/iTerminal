using iConsole.Media.Audio;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Chooses the usable audio backend for this machine and answers "what can I play through?" for the
/// device selector. Availability probing costs a native library load or a child process, so it runs
/// on a background task and results are cached until <see cref="Refresh"/> is called (which the
/// player does when the user asks to rescan, so hot-plugged devices show up without a restart).
///
/// <para>The readers never block on a probe: until the first one lands they answer with the empty
/// state and <see cref="IsProbing"/> is true, and <see cref="DevicesChanged"/> is raised from the
/// probe thread when a probe lands, so the screen can be repainted. Callers on a render or key path
/// must not wait for it.</para>
///
/// <para>Implementations never throw: with no backend at all, <see cref="ActiveBackendName"/> is
/// empty and <see cref="GetDevices"/> returns nothing, and the player reports that instead of
/// failing.</para>
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>Name of the selected backend ("WinMM", "PulseAudio", "ALSA"), or empty when none or not yet probed.</summary>
    string ActiveBackendName { get; }

    /// <summary>Whether any backend is usable — false means this machine has no audio output, or the probe has not landed yet.</summary>
    bool HasAudioOutput { get; }

    /// <summary>True while a probe is running in the background, including the first one.</summary>
    bool IsProbing { get; }

    /// <summary>Output endpoints of the active backend, default first. Empty until the first probe lands.</summary>
    IReadOnlyList<AudioDevice> GetDevices();

    /// <summary>
    /// Starts a background re-probe of the backends and the device list and returns at once. The
    /// previous list stays readable until the new one lands, then <see cref="DevicesChanged"/> is raised.
    /// </summary>
    void Refresh();

    /// <summary>Raised on a probe thread, not the UI thread, each time a probe lands.</summary>
    event EventHandler? DevicesChanged;

    /// <summary>Creates an unopened sink on the active backend, or null when there is no backend.</summary>
    IAudioSink? CreateSink();
}
