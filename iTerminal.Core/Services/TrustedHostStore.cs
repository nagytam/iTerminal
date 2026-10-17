using System.Text.Json;

namespace iTerminal.Core.Services;

/// <summary>How a presented host identity compares with the one remembered for that host.</summary>
public enum HostTrust
{
    /// <summary>Never seen before; the identity was recorded (trust on first use).</summary>
    New,

    /// <summary>Matches what was recorded.</summary>
    Trusted,

    /// <summary>Differs from what was recorded: a rebuilt server, or someone in the middle.</summary>
    Changed,
}

/// <summary>
/// The identities (SSH host keys, FTPS certificates) the user has connected to before. A host seen for the
/// first time is remembered; a host whose identity later differs is refused until the old entry is forgotten.
/// </summary>
public interface ITrustedHostStore
{
    /// <summary>Compares <paramref name="fingerprint"/> with the one recorded for <paramref name="hostId"/> and records it when there is none.</summary>
    /// <param name="hostId">Kind, host and port, e.g. <c>ssh:example.org:22</c>.</param>
    /// <param name="fingerprint">Algorithm and digest of the key or certificate.</param>
    /// <param name="recorded">The fingerprint on file when the result is <see cref="HostTrust.Changed"/>.</param>
    HostTrust Check(string hostId, string fingerprint, out string? recorded);

    /// <summary>Forgets a host so the next connection records whatever it presents.</summary>
    bool Forget(string hostId);
}

/// <summary>
/// <see cref="ITrustedHostStore"/> in one JSON file (<c>known_hosts.json</c> in the data folder). A file that
/// cannot be read is treated as empty and left alone until the next new host is written; a write that fails
/// still lets the connection through for this run, because the alternative is a drive that cannot be used.
/// </summary>
public sealed class TrustedHostStore : ITrustedHostStore
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private Dictionary<string, string>? _hosts;

    public TrustedHostStore(string path)
    {
        _path = path;
    }

    /// <summary>A store that lives in memory only, for a run with nowhere to keep it.</summary>
    public TrustedHostStore()
        : this(Path.Combine(Path.GetTempPath(), $"iTerminal.KnownHosts.{Guid.NewGuid():N}.json"))
    {
    }

    public HostTrust Check(string hostId, string fingerprint, out string? recorded)
    {
        lock (_lock)
        {
            var hosts = Load();
            if (hosts.TryGetValue(hostId, out var known))
            {
                recorded = known;
                return string.Equals(known, fingerprint, StringComparison.Ordinal) ? HostTrust.Trusted : HostTrust.Changed;
            }

            recorded = null;
            hosts[hostId] = fingerprint;
            Save(hosts);
            return HostTrust.New;
        }
    }

    public bool Forget(string hostId)
    {
        lock (_lock)
        {
            var hosts = Load();
            if (!hosts.Remove(hostId))
            {
                return false;
            }

            Save(hosts);
            return true;
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_hosts is not null)
        {
            return _hosts;
        }

        try
        {
            var stored = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                : null;
            _hosts = stored is null ? null : new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _hosts = null;
        }

        return _hosts ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save(Dictionary<string, string> hosts)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(hosts, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this run.
        }
    }
}
