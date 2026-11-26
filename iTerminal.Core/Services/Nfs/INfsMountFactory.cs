namespace iTerminal.Core.Services.Nfs;

/// <summary>Asks mountd which exports a server offers, and mounts one of them. The NfsSharp client sits behind this.</summary>
public interface INfsMountFactory
{
    Task<IReadOnlyList<string>> ListExportsAsync(string host, CancellationToken cancellationToken);

    Task<INfsMount> MountAsync(string host, string export, CancellationToken cancellationToken);
}

/// <summary>A system where no NFS server can be reached from the test sandbox or the build; every call says why.</summary>
public sealed class UnavailableNfsMountFactory(string reason) : INfsMountFactory
{
    public Task<IReadOnlyList<string>> ListExportsAsync(string host, CancellationToken cancellationToken) => throw new NotSupportedException(reason);

    public Task<INfsMount> MountAsync(string host, string export, CancellationToken cancellationToken) => throw new NotSupportedException(reason);
}
