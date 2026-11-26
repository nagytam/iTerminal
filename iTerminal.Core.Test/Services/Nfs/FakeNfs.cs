using iTerminal.Core.Services.Nfs;

namespace iTerminal.Core.Test.Services.Nfs;

/// <summary>An in-memory NFS export: folders, files and what was asked of them. Nothing here reaches a server.</summary>
internal sealed class FakeNfsMount : INfsMount
{
    public Dictionary<string, NfsRemoteKind> Kinds { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<NfsRemoteEntry>> Listings { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public bool Disposed { get; private set; }

    public Exception? Fail { get; set; }

    private void Check()
    {
        if (Fail is not null)
        {
            throw Fail;
        }
    }

    public Task<IReadOnlyList<NfsRemoteEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"list {path}");
        return Task.FromResult<IReadOnlyList<NfsRemoteEntry>>(Listings.TryGetValue(path, out var rows) ? rows : []);
    }

    public Task<NfsRemoteKind> KindAsync(string path, CancellationToken cancellationToken)
    {
        Check();
        return Task.FromResult(Kinds.TryGetValue(path, out var kind) ? kind : NfsRemoteKind.None);
    }

    public async Task ReadAsync(string path, Stream destination, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"read {path}");
        var bytes = Files[path];
        await destination.WriteAsync(bytes, cancellationToken);
    }

    public async Task WriteAsync(string path, Stream source, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"write {path}");
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        Files[path] = buffer.ToArray();
        Kinds[path] = NfsRemoteKind.File;
    }

    public Task CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"mkdir {path}");
        Kinds[path] = NfsRemoteKind.Folder;
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"rm {path}");
        Files.Remove(path);
        Kinds.Remove(path);
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"rmdir {path} recursive={recursive}");
        Kinds.Remove(path);
        return Task.CompletedTask;
    }

    public Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        Check();
        Calls.Add($"mv {from} {to}");
        if (Files.Remove(from, out var bytes))
        {
            Files[to] = bytes;
            Kinds[to] = NfsRemoteKind.File;
            Kinds.Remove(from);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Hands out one <see cref="FakeNfsMount"/> per (host, export) and counts how often each was mounted.</summary>
internal sealed class FakeNfsMountFactory : INfsMountFactory
{
    public List<string> Exports { get; } = [];

    public Dictionary<string, FakeNfsMount> Mounts { get; } = new(StringComparer.Ordinal);

    public int MountCount { get; private set; }

    public Exception? MountFailure { get; set; }

    public Task<IReadOnlyList<string>> ListExportsAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Exports);

    public Task<INfsMount> MountAsync(string host, string export, CancellationToken cancellationToken)
    {
        if (MountFailure is not null)
        {
            throw MountFailure;
        }

        MountCount++;
        var key = host + "|" + export;
        if (!Mounts.TryGetValue(key, out var mount))
        {
            mount = new FakeNfsMount();
            Mounts[key] = mount;
        }

        return Task.FromResult<INfsMount>(mount);
    }

    public FakeNfsMount On(string host, string export) => Mounts[host + "|" + export];
}
