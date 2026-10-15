using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Audio;

/// <summary>
/// A live connection to one output device, fed signed 16-bit PCM. <see cref="Write"/> blocks while
/// the device is busy, which is deliberate: the sink is the playback clock, so the pump thread
/// paces itself against real playback instead of needing a timer.
///
/// <para>Only the pump thread touches a sink, except <see cref="Stop"/>, which any thread may call
/// to unblock a pending <see cref="Write"/>.</para>
/// </summary>
public interface IAudioSink : IDisposable
{
    /// <summary>Opens the device. Throws when the device is unavailable or the format is rejected.</summary>
    void Open(PcmFormat format, string? deviceId);

    /// <summary>
    /// Queues PCM for playback, blocking until the device has room. Returns the number of bytes
    /// accepted, which is less than the input only when the sink was stopped mid-write.
    /// </summary>
    int Write(ReadOnlySpan<byte> pcm);

    /// <summary>Blocks until everything already queued has finished playing.</summary>
    void Drain();

    /// <summary>Drops queued audio so a seek or track change is heard immediately.</summary>
    void Flush();

    /// <summary>Halts output and unblocks any in-flight <see cref="Write"/>. Callable from any thread.</summary>
    void Stop();

    /// <summary>Suspends output without discarding what is queued. No-op on backends that cannot.</summary>
    void Pause();

    /// <summary>Resumes after <see cref="Pause"/>.</summary>
    void Resume();

    /// <summary>
    /// How much audio is queued but not yet heard. Subtracted from the decoder position so the
    /// displayed elapsed time matches what the speakers are actually playing.
    /// </summary>
    TimeSpan BufferedDuration { get; }
}

/// <summary>
/// One platform audio API. Backends are registered together and probed at runtime — the machine,
/// not the build, decides which one is usable (a Linux box may have PulseAudio, bare ALSA, or
/// neither), so this cannot be a compile-time or registration-time choice.
/// </summary>
public interface IAudioOutputBackend
{
    /// <summary>Short identifier shown in the UI, e.g. "WinMM", "PulseAudio", "ALSA".</summary>
    string Name { get; }

    /// <summary>
    /// Lower runs first. Backends that offer real device selection and low latency rank ahead of
    /// fallbacks.
    /// </summary>
    int Priority { get; }

    /// <summary>Whether this backend can be used on this machine right now. Must never throw.</summary>
    bool IsAvailable();

    /// <summary>Output endpoints, most useful first. Best-effort; empty when enumeration fails.</summary>
    IReadOnlyList<AudioDevice> GetDevices();

    /// <summary>Creates an unopened sink for this backend.</summary>
    IAudioSink CreateSink();
}
