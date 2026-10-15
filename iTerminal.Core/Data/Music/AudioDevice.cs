namespace iTerminal.Core.Data.Music;

/// <summary>
/// An output endpoint the engine can render to. <see cref="Id"/> is backend-specific and opaque to
/// the UI: a WinMM device index on Windows ("0", "1", …), a PulseAudio sink name on Linux, or an
/// ALSA device string ("plughw:1,0"). <see cref="IsDefault"/> marks the system default, which is
/// what the engine uses when no device has been chosen or a remembered device has disappeared.
/// </summary>
public sealed record AudioDevice(string Id, string Name, string Backend)
{
    public bool IsDefault { get; init; }

    /// <summary>Label for the device selector, e.g. <c>"Speakers (Realtek) [default]"</c>.</summary>
    public string DisplayName => IsDefault ? $"{Name} [default]" : Name;
}
