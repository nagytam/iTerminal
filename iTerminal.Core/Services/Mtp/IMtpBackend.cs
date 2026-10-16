namespace iTerminal.Core.Services.Mtp;

/// <summary>One row of an MTP folder as the platform reported it.</summary>
public sealed record MtpItem(string Name, bool IsFolder, long Size, DateTime? Modified);

/// <summary>
/// What the platform's own MTP stack is asked for: GVfs through <c>gio</c> on Linux, the Windows
/// shell namespace through PowerShell on Windows. There is no MTP library here — the protocol is
/// one a phone only speaks to the operating system's own driver stack, which has claimed the device
/// by the time anything else could open it — so this is the seam <see cref="MtpDriveService"/> is
/// written against and tests replace.
///
/// <para>A <c>path</c> is relative to the device and starts with the storage:
/// <c>Internal shared storage/DCIM/Camera</c>. Storage is a folder like any other from here down,
/// which is how both platforms present it.</para>
/// </summary>
public interface IMtpBackend
{
    /// <summary>Empty when this system can talk MTP; otherwise why it cannot, in words.</summary>
    string Unavailable { get; }

    /// <summary>The names of the connected devices.</summary>
    Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken);

    /// <summary>The storages of <paramref name="device"/> ("Internal shared storage", an SD card).</summary>
    Task<IReadOnlyList<string>> GetStoragesAsync(string device, CancellationToken cancellationToken);

    Task<IReadOnlyList<MtpItem>> ListAsync(string device, string path, CancellationToken cancellationToken);

    Task DownloadAsync(string device, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);

    Task UploadAsync(string device, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken);

    Task CreateFolderAsync(string device, string path, CancellationToken cancellationToken);

    /// <summary>Deletes one file, or one folder that is empty — the delete pipeline empties a folder first.</summary>
    Task DeleteAsync(string device, string path, bool isFolder, CancellationToken cancellationToken);
}

/// <summary>A system with no MTP stack this app can drive (macOS has none a command line can reach).</summary>
public sealed class UnavailableMtpBackend(string reason) : IMtpBackend
{
    public string Unavailable => reason;

    private NotSupportedException Refuse() => new(reason);

    public Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken) => throw Refuse();

    public Task<IReadOnlyList<string>> GetStoragesAsync(string device, CancellationToken cancellationToken) => throw Refuse();

    public Task<IReadOnlyList<MtpItem>> ListAsync(string device, string path, CancellationToken cancellationToken) => throw Refuse();

    public Task DownloadAsync(string device, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) => throw Refuse();

    public Task UploadAsync(string device, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw Refuse();

    public Task CreateFolderAsync(string device, string path, CancellationToken cancellationToken) => throw Refuse();

    public Task DeleteAsync(string device, string path, bool isFolder, CancellationToken cancellationToken) => throw Refuse();
}
