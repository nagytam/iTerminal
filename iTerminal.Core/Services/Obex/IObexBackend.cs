namespace iTerminal.Core.Services.Obex;

/// <summary>One row of an OBEX folder as the phone reported it.</summary>
public sealed record ObexItem(string Name, bool IsFolder, long Size, DateTime? Modified);

/// <summary>
/// What the Bluetooth stack's OBEX file transfer is asked for: BlueZ's <c>obexd</c> over D-Bus on
/// Linux. It is the seam <see cref="ObexDriveService"/> is written against and tests replace, the way
/// <see cref="Mtp.IMtpBackend"/> is for MTP.
///
/// <para>A <c>path</c> is relative to the device's own file root and has no leading or trailing slash;
/// the empty path is the root. A device has to accept the FTP service (most phones ask the user to
/// approve a browse on the phone itself), so a refusal is an error with the device's words in it.</para>
/// </summary>
public interface IObexBackend
{
    /// <summary>Empty when this system can talk OBEX; otherwise why it cannot, in words.</summary>
    string Unavailable { get; }

    Task<IReadOnlyList<ObexItem>> ListAsync(string address, string path, CancellationToken cancellationToken);

    Task DownloadAsync(string address, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);

    Task UploadAsync(string address, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken);

    Task CreateFolderAsync(string address, string path, CancellationToken cancellationToken);

    /// <summary>Deletes one file, or one folder that is empty — the delete pipeline empties a folder first.</summary>
    Task DeleteAsync(string address, string path, bool isFolder, CancellationToken cancellationToken);
}

/// <summary>A system with no OBEX stack this app can drive (Windows has no OBEX browse API a command line can reach).</summary>
public sealed class UnavailableObexBackend(string reason) : IObexBackend
{
    public string Unavailable => reason;

    private NotSupportedException Refuse() => new(reason);

    public Task<IReadOnlyList<ObexItem>> ListAsync(string address, string path, CancellationToken cancellationToken) => throw Refuse();

    public Task DownloadAsync(string address, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) => throw Refuse();

    public Task UploadAsync(string address, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw Refuse();

    public Task CreateFolderAsync(string address, string path, CancellationToken cancellationToken) => throw Refuse();

    public Task DeleteAsync(string address, string path, bool isFolder, CancellationToken cancellationToken) => throw Refuse();
}
