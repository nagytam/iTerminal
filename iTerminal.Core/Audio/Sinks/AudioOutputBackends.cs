using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Audio.Sinks;

/// <summary>Windows waveOut backend. See <see cref="WinMmAudioSink"/> for why waveOut over WASAPI.</summary>
public sealed class WinMmAudioOutputBackend : IAudioOutputBackend
{
    public string Name => "WinMM";

    public int Priority => 0;

    public bool IsAvailable() => OperatingSystem.IsWindows() && WinMmAudioSink.IsAvailable();

    public IReadOnlyList<AudioDevice> GetDevices() =>
        OperatingSystem.IsWindows() ? WinMmAudioSink.GetDevices() : [];

    public IAudioSink CreateSink()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The WinMM backend only runs on Windows.");
        }

        return new WinMmAudioSink();
    }
}

/// <summary>PulseAudio/PipeWire backend — the primary Linux path.</summary>
public sealed class PulseAudioOutputBackend : IAudioOutputBackend
{
    public string Name => "PulseAudio";

    public int Priority => 10;

    public bool IsAvailable() => OperatingSystem.IsLinux() && PulseAudioSink.IsAvailable();

    public IReadOnlyList<AudioDevice> GetDevices() =>
        OperatingSystem.IsLinux() ? PulseAudioSink.GetDevices() : [];

    public IAudioSink CreateSink()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The PulseAudio backend only runs on Linux.");
        }

        return new PulseAudioSink();
    }
}

/// <summary>ALSA-via-aplay backend, used when libpulse is absent.</summary>
public sealed class AplayAudioOutputBackend : IAudioOutputBackend
{
    public string Name => "ALSA";

    public int Priority => 20;

    public bool IsAvailable() => OperatingSystem.IsLinux() && AplayAudioSink.IsAvailable();

    public IReadOnlyList<AudioDevice> GetDevices() =>
        OperatingSystem.IsLinux() ? AplayAudioSink.GetDevices() : [];

    public IAudioSink CreateSink()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The ALSA backend only runs on Linux.");
        }

        return new AplayAudioSink();
    }
}
