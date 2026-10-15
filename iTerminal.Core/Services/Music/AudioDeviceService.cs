using iTerminal.Core.Audio;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Picks the first available backend by <see cref="IAudioOutputBackend.Priority"/>. Selection is
/// runtime rather than registration-time on purpose: the same Linux binary may find PulseAudio on
/// one machine and only ALSA on the next, and a Windows session can have no output device at all.
/// </summary>
public sealed class AudioDeviceService : IAudioDeviceService
{
    private readonly IReadOnlyList<IAudioOutputBackend> _backends;
    private readonly object _syncLock = new();

    private IAudioOutputBackend? _activeBackend;
    private IReadOnlyList<AudioDevice>? _devices;
    private bool _probed;

    public AudioDeviceService(IEnumerable<IAudioOutputBackend> backends)
    {
        _backends = [.. (backends ?? []).OrderBy(backend => backend.Priority)];
    }

    public string ActiveBackendName => ResolveBackend()?.Name ?? string.Empty;

    public bool HasAudioOutput => ResolveBackend() is not null;

    public IReadOnlyList<AudioDevice> GetDevices()
    {
        lock (_syncLock)
        {
            if (_devices is not null)
            {
                return _devices;
            }
        }

        var backend = ResolveBackend();
        IReadOnlyList<AudioDevice> devices;
        try
        {
            devices = backend?.GetDevices() ?? [];
        }
        catch
        {
            devices = [];
        }

        lock (_syncLock)
        {
            _devices = devices;
            return _devices;
        }
    }

    public void Refresh()
    {
        lock (_syncLock)
        {
            _probed = false;
            _activeBackend = null;
            _devices = null;
        }
    }

    public IAudioSink? CreateSink()
    {
        var backend = ResolveBackend();
        if (backend is null)
        {
            return null;
        }

        try
        {
            return backend.CreateSink();
        }
        catch
        {
            return null;
        }
    }

    private IAudioOutputBackend? ResolveBackend()
    {
        lock (_syncLock)
        {
            if (_probed)
            {
                return _activeBackend;
            }
        }

        IAudioOutputBackend? resolved = null;
        foreach (var backend in _backends)
        {
            try
            {
                if (backend.IsAvailable())
                {
                    resolved = backend;
                    break;
                }
            }
            catch
            {
                // A backend that blows up while probing is simply not a candidate.
            }
        }

        lock (_syncLock)
        {
            _activeBackend = resolved;
            _probed = true;
            return _activeBackend;
        }
    }
}
