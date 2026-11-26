namespace iTerminal.Core.Services.Nfs;

/// <summary>One row of an NFS folder as the server reported it.</summary>
public sealed record NfsRemoteEntry(string Name, bool IsFolder, long Size, DateTime? Modified);

/// <summary>What a path on an export is, or that nothing is there.</summary>
public enum NfsRemoteKind
{
    None,
    File,
    Folder,
}

/// <summary>
/// One mounted NFSv3 export. Every path is relative to the export's root, and <c>.</c> is the root itself.
/// This is the seam <see cref="NfsDriveService"/> is written against, so the tests replace the network with a fake.
/// </summary>
public interface INfsMount : IAsyncDisposable
{
    Task<IReadOnlyList<NfsRemoteEntry>> ListAsync(string path, CancellationToken cancellationToken);

    Task<NfsRemoteKind> KindAsync(string path, CancellationToken cancellationToken);

    Task ReadAsync(string path, Stream destination, CancellationToken cancellationToken);

    /// <summary>Creates the file, or truncates it, and writes the whole of <paramref name="source"/> into it.</summary>
    Task WriteAsync(string path, Stream source, CancellationToken cancellationToken);

    Task CreateFolderAsync(string path, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task DeleteFolderAsync(string path, bool recursive, CancellationToken cancellationToken);

    Task MoveAsync(string from, string to, CancellationToken cancellationToken);
}
