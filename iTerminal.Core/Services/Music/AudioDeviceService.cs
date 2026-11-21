using iConsole.Media.Audio;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Picks the first available backend by <see cref="IAudioOutputBackend.Priority"/>. Selection is
/// runtime rather than registration-time on purpose: the same Linux binary may find PulseAudio on
/// one machine and only ALSA on the next, and a Windows session can have no output device at all.
///
/// <para>Probing runs on a thread-pool task. The readers (<see cref="ActiveBackendName"/>,
/// <see cref="HasAudioOutput"/>, <see cref="GetDevices"/>) only read the cache, so a key handler or a
/// <c>Draw</c> never waits on <c>pactl</c> or <c>aplay</c>. The one caller that probes inline is
/// <see cref="CreateSink"/>, which runs on the playback pump thread and only when no probe has landed yet.</para>
/// </summary>
public sealed class AudioDeviceService : IAudioDeviceService
{
    private readonly IReadOnlyList<IAudioOutputBackend> _backends;
    private readonly object _syncLock = new();

    private IAudioOutputBackend? _activeBackend;
    private IReadOnlyList<AudioDevice> _devices = [];
    private bool _resolved;
    private bool _started;
    private bool _probing;
    private int _generation;

    public AudioDeviceService(IEnumerable<IAudioOutputBackend> backends)
    {
        _backends = [.. (backends ?? []).OrderBy(backend => backend.Priority)];
    }

    /// <inheritdoc />
    public event EventHandler? DevicesChanged;

    public string ActiveBackendName
    {
        get
        {
            StartFirstProbe();
            lock (_syncLock)
            {
                return _activeBackend?.Name ?? string.Empty;
            }
        }
    }

    public bool HasAudioOutput
    {
        get
        {
            StartFirstProbe();
            lock (_syncLock)
            {
                return _activeBackend is not null;
            }
        }
    }

    public bool IsProbing
    {
        get
        {
            StartFirstProbe();
            lock (_syncLock)
            {
                return _probing;
            }
        }
    }

    public IReadOnlyList<AudioDevice> GetDevices()
    {
        StartFirstProbe();
        lock (_syncLock)
        {
            return _devices;
        }
    }

    public void Refresh() => StartProbe(onlyIfNotStarted: false);

    public IAudioSink? CreateSink()
    {
        var backend = SinkBackend();
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

    private void StartFirstProbe() => StartProbe(onlyIfNotStarted: true);

    /// <summary>
    /// Launches a probe on the thread pool. Each launch takes a new generation number, so a probe
    /// that finishes after a newer one was started discards its result instead of overwriting it.
    /// </summary>
    private void StartProbe(bool onlyIfNotStarted)
    {
        int generation;
        lock (_syncLock)
        {
            if (onlyIfNotStarted && _started)
            {
                return;
            }

            _started = true;
            _probing = true;
            generation = ++_generation;
        }

        _ = Task.Run(() => Probe(generation));
    }

    private void Probe(int generation)
    {
        var backend = FindAvailableBackend();
        IReadOnlyList<AudioDevice> devices = [];
        if (backend is not null)
        {
            try
            {
                devices = backend.GetDevices();
            }
            catch
            {
                devices = [];
            }
        }

        bool landed;
        lock (_syncLock)
        {
            landed = generation == _generation;
            if (landed)
            {
                _activeBackend = backend;
                _devices = devices;
                _resolved = true;
                _probing = false;
            }
        }

        if (landed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IAudioOutputBackend? SinkBackend()
    {
        lock (_syncLock)
        {
            if (_resolved)
            {
                return _activeBackend;
            }
        }

        // The pump thread asks for a sink while it plays, so it may probe inline. This only happens
        // before the first background probe lands; afterwards the cached backend is used.
        var backend = FindAvailableBackend();
        lock (_syncLock)
        {
            if (!_resolved)
            {
                _activeBackend = backend;
                _resolved = true;
            }

            return _activeBackend;
        }
    }

    private IAudioOutputBackend? FindAvailableBackend()
    {
        foreach (var backend in _backends)
        {
            try
            {
                if (backend.IsAvailable())
                {
                    return backend;
                }
            }
            catch
            {
                // A backend that blows up while probing is simply not a candidate.
            }
        }

        return null;
    }
}
