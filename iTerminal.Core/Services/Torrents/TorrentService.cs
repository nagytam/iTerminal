using MonoTorrent;
using MonoTorrent.Client;
using Serilog;
using System.Text.Json;

namespace iTerminal.Core.Services.Torrents;

/// <summary>Where a torrent is in its life, as the manager shows it.</summary>
public enum TorrentStatus
{
    Starting,
    Metadata,
    Checking,
    Downloading,
    Seeding,
    Paused,
    Stopped,
    Error,
}

/// <summary>One torrent as the manager lists it: a snapshot, safe to hold on to.</summary>
public sealed record TorrentItem(
    string Id,
    string Name,
    TorrentStatus Status,
    double Progress,
    long Size,
    long DownloadRate,
    long UploadRate,
    int Peers,
    string SavePath,
    string? Error);

public interface ITorrentService
{
    /// <summary>Where a torrent goes when no folder is given.</summary>
    string DefaultDownloadDirectory { get; }

    /// <summary>Every torrent, in the order added.</summary>
    IReadOnlyList<TorrentItem> Snapshot();

    /// <summary>Adds a magnet link, a <c>.torrent</c> file or the URL of one, and starts it. Returns the new item's id.</summary>
    Task<string> AddAsync(string source, string? saveDirectory = null, CancellationToken cancellationToken = default);

    /// <summary>Starts the torrents saved by an earlier run that were not paused. Does nothing when there are none.</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(string id);
    Task ResumeAsync(string id);

    /// <summary>Forgets the torrent; with <paramref name="deleteData"/> its downloaded files go too.</summary>
    Task RemoveAsync(string id, bool deleteData);

    /// <summary>Stops everything and closes the listening sockets.</summary>
    Task ShutdownAsync();
}

/// <summary>The three kinds of thing the add prompt accepts.</summary>
public enum TorrentSourceKind
{
    Invalid,
    Magnet,
    File,
    Url,
}

public static class TorrentSource
{
    public static TorrentSourceKind Classify(string? text)
    {
        string source = Clean(text);
        if (source.Length == 0)
        {
            return TorrentSourceKind.Invalid;
        }

        if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return MagnetLink.TryParse(source, out _) ? TorrentSourceKind.Magnet : TorrentSourceKind.Invalid;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
        {
            return TorrentSourceKind.Url;
        }

        return TorrentSourceKind.File;
    }

    /// <summary>Trims the spaces and the quotes a file manager puts around a dragged or copied path.</summary>
    public static string Clean(string? text) => (text ?? string.Empty).Trim().Trim('"');
}

/// <summary>
/// The BitTorrent client: a single MonoTorrent engine, created the first time something is added or restored (so a
/// run that never opens the manager opens no sockets). The torrents are kept in the settings — magnet links as they are,
/// <c>.torrent</c> files copied next to the database — so they come back after a restart; the pieces already
/// downloaded are found again by the engine's own fast-resume files in the cache folder.
/// </summary>
public sealed class TorrentService : ITorrentService, IAsyncDisposable, IDisposable
{
    public const string ItemsKey = "BitTorrent.Items";
    public const string DownloadFolderKey = "BitTorrent.DownloadFolder";
    private const int MaxTorrentFileBytes = 10 * 1024 * 1024;

    private sealed record Saved(string Id, string Source, string SavePath, bool Paused);

    private sealed class Entry(string id, string source, string savePath, TorrentManager manager, bool paused)
    {
        public string Id { get; } = id;
        public string Source { get; } = source;
        public string SavePath { get; } = savePath;
        public TorrentManager Manager { get; } = manager;
        public bool Paused { get; set; } = paused;
    }

    private readonly ISettingsService _settings;
    private readonly string _dataDirectory;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Entry> _entries = [];
    private readonly Lock _sync = new();

    private ClientEngine? _engine;
    private bool _restored;

    public TorrentService(ISettingsService settings, string dataDirectory, ILogger? logger = null)
    {
        _settings = settings;
        _dataDirectory = dataDirectory;
        _logger = logger;
    }

    public string DefaultDownloadDirectory =>
        _settings.Get(DownloadFolderKey) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private string TorrentsDirectory => Path.Combine(_dataDirectory, "torrents");

    public IReadOnlyList<TorrentItem> Snapshot()
    {
        lock (_sync)
        {
            return [.. _entries.Select(ToItem)];
        }
    }

    private static TorrentItem ToItem(Entry entry)
    {
        TorrentManager manager = entry.Manager;
        string? error = manager.Error?.Exception?.Message ?? manager.Error?.Reason.ToString();
        TorrentStatus status = manager.State switch
        {
            TorrentState.Error => TorrentStatus.Error,
            TorrentState.Metadata => TorrentStatus.Metadata,
            TorrentState.Hashing or TorrentState.HashingPaused => TorrentStatus.Checking,
            TorrentState.Downloading => TorrentStatus.Downloading,
            TorrentState.Seeding => TorrentStatus.Seeding,
            TorrentState.Paused => TorrentStatus.Paused,
            TorrentState.Stopped or TorrentState.Stopping => entry.Paused ? TorrentStatus.Paused : TorrentStatus.Stopped,
            _ => TorrentStatus.Starting,
        };

        return new TorrentItem(
            entry.Id,
            string.IsNullOrWhiteSpace(manager.Name) ? entry.Id : manager.Name,
            status,
            Math.Clamp(manager.Progress, 0, 100),
            manager.Torrent?.Size ?? 0,
            manager.Monitor.DownloadRate,
            manager.Monitor.UploadRate,
            manager.OpenConnections,
            entry.SavePath,
            error);
    }

    public async Task<string> AddAsync(string source, string? saveDirectory = null, CancellationToken cancellationToken = default)
    {
        string text = TorrentSource.Clean(source);
        string savePath = string.IsNullOrWhiteSpace(saveDirectory) ? DefaultDownloadDirectory : saveDirectory.Trim().Trim('"');

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ClientEngine engine = EnsureEngine();
            (string id, string stored, TorrentManager manager) = await CreateManagerAsync(engine, text, savePath, cancellationToken);
            Directory.CreateDirectory(savePath);
            lock (_sync)
            {
                _entries.Add(new Entry(id, stored, savePath, manager, paused: false));
            }

            await manager.StartAsync();
            Persist();
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Id, string Stored, TorrentManager Manager)> CreateManagerAsync(
        ClientEngine engine, string source, string savePath, CancellationToken cancellationToken)
    {
        switch (TorrentSource.Classify(source))
        {
            case TorrentSourceKind.Magnet:
            {
                MagnetLink link = MagnetLink.Parse(source);
                string id = IdOf(link.InfoHashes);
                EnsureNew(id);
                return (id, source, await engine.AddAsync(link, savePath));
            }
            case TorrentSourceKind.Url:
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
                using HttpResponseMessage response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxTorrentFileBytes)
                {
                    throw new InvalidOperationException("That file is too large to be a .torrent.");
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return await AddMetadataAsync(engine, bytes, savePath);
            }
            case TorrentSourceKind.File:
            {
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException($"There is no file \"{source}\".");
                }

                if (new FileInfo(source).Length > MaxTorrentFileBytes)
                {
                    throw new InvalidOperationException("That file is too large to be a .torrent.");
                }

                return await AddMetadataAsync(engine, await File.ReadAllBytesAsync(source, cancellationToken), savePath);
            }
            default:
                throw new InvalidOperationException("Give a magnet link, a .torrent file or the URL of one.");
        }
    }

    private async Task<(string Id, string Stored, TorrentManager Manager)> AddMetadataAsync(ClientEngine engine, byte[] bytes, string savePath)
    {
        Torrent torrent;
        try
        {
            torrent = Torrent.Load(bytes);
        }
        catch (Exception exception) when (exception is TorrentException or ArgumentException or FormatException or IOException)
        {
            throw new InvalidOperationException("That is not a valid .torrent file.", exception);
        }

        string id = IdOf(torrent.InfoHashes);
        EnsureNew(id);
        Directory.CreateDirectory(TorrentsDirectory);
        string copy = Path.Combine(TorrentsDirectory, id + ".torrent");
        await File.WriteAllBytesAsync(copy, bytes);
        return (id, copy, await engine.AddAsync(torrent, savePath));
    }

    private static string IdOf(InfoHashes hashes) => (hashes.V1OrV2).ToHex().ToLowerInvariant();

    private void EnsureNew(string id)
    {
        lock (_sync)
        {
            if (_entries.Any(entry => entry.Id == id))
            {
                throw new InvalidOperationException("That torrent is already in the list.");
            }
        }
    }

    private ClientEngine EnsureEngine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        string cache = Path.Combine(_dataDirectory, "torrent-cache");
        Directory.CreateDirectory(cache);
        EngineSettings settings = new EngineSettingsBuilder { CacheDirectory = cache }.ToSettings();
        _engine = new ClientEngine(settings);
        return _engine;
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            List<Saved> saved = Load();
            if (saved.Count == 0)
            {
                return;
            }

            ClientEngine engine = EnsureEngine();
            foreach (Saved item in saved)
            {
                try
                {
                    (string id, string stored, TorrentManager manager) = await CreateManagerAsync(engine, item.Source, item.SavePath, cancellationToken);
                    lock (_sync)
                    {
                        _entries.Add(new Entry(id, stored, item.SavePath, manager, item.Paused));
                    }

                    if (!item.Paused)
                    {
                        await manager.StartAsync();
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One torrent that cannot come back (its .torrent file is gone) must not keep the others down.
                    _logger?.Warning(exception, "Could not restore the torrent {Id}", item.Id);
                }
            }

            Persist();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PauseAsync(string id)
    {
        Entry? entry = Find(id);
        if (entry is null)
        {
            return;
        }

        entry.Paused = true;
        await entry.Manager.StopAsync();
        Persist();
    }

    public async Task ResumeAsync(string id)
    {
        Entry? entry = Find(id);
        if (entry is null)
        {
            return;
        }

        entry.Paused = false;
        await entry.Manager.StartAsync();
        Persist();
    }

    public async Task RemoveAsync(string id, bool deleteData)
    {
        Entry? entry = Find(id);
        if (entry is null)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            lock (_sync)
            {
                _entries.Remove(entry);
            }

            if (entry.Manager.State != TorrentState.Stopped)
            {
                await entry.Manager.StopAsync();
            }

            if (_engine is not null)
            {
                await _engine.RemoveAsync(entry.Manager, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);
            }

            if (entry.Source.StartsWith(TorrentsDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(entry.Source))
            {
                File.Delete(entry.Source);
            }

            Persist();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        if (_engine is null)
        {
            return;
        }

        List<Entry> entries;
        lock (_sync)
        {
            entries = [.. _entries];
        }

        foreach (Entry entry in entries)
        {
            if (entry.Manager.State != TorrentState.Stopped)
            {
                await entry.Manager.StopAsync();
            }
        }

        await _engine.StopAllAsync();
        _engine.Dispose();
        _engine = null;
    }

    /// <summary>For a container disposed synchronously; the engine only exists once something was added, so this is a no-op otherwise.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ShutdownAsync();
        }
        catch (Exception exception)
        {
            _logger?.Warning(exception, "The torrent engine did not shut down cleanly");
        }
    }

    private Entry? Find(string id)
    {
        lock (_sync)
        {
            return _entries.FirstOrDefault(entry => entry.Id == id);
        }
    }

    private List<Saved> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<Saved>>(_settings.Get(ItemsKey) ?? "[]") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Persist()
    {
        List<Saved> saved;
        lock (_sync)
        {
            saved = [.. _entries.Select(entry => new Saved(entry.Id, entry.Source, entry.SavePath, entry.Paused))];
        }

        _settings.Set(ItemsKey, JsonSerializer.Serialize(saved));
    }
}
