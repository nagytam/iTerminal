using NfsSharp.Client;
using NfsSharp.Protocol;

namespace iTerminal.Core.Services.Nfs;

/// <summary>
/// The NfsSharp.Client adapter: the only file that names the library. NFSv3 over TCP with AUTH_SYS, the identity taken
/// from the <c>Nfs.UserId</c> and <c>Nfs.GroupId</c> settings (1000 and 1000 when unset). NFS has no passwords, so
/// nothing is read from the vault or the URL. The client never asks for a privileged source port.
/// </summary>
public sealed class NfsV3MountFactory(ISettingsService settings) : INfsMountFactory
{
    public const string UserIdSetting = "Nfs.UserId";
    public const string GroupIdSetting = "Nfs.GroupId";
    public const uint DefaultUserId = 1000;
    public const uint DefaultGroupId = 1000;

    public async Task<IReadOnlyList<string>> ListExportsAsync(string host, CancellationToken cancellationToken)
    {
        var exports = await NfsV3Client.ListExportsAsync(host, Options(), cancellationToken);
        return [.. exports.Select(export => export.Path)];
    }

    public async Task<INfsMount> MountAsync(string host, string export, CancellationToken cancellationToken)
    {
        var client = await NfsV3Client.ConnectAsync(host, export, Options(), cancellationToken);
        return new NfsV3Mount(client);
    }

    private NfsClientOptions Options() => new()
    {
        UserId = Identity(UserIdSetting, DefaultUserId),
        GroupId = Identity(GroupIdSetting, DefaultGroupId),
        UsePrivilegedSourcePort = false,
        CommandTimeout = TimeSpan.FromSeconds(30),
    };

    private uint Identity(string key, uint fallback) => uint.TryParse(settings.Get(key), out var value) ? value : fallback;

    /// <summary>One mounted export, delegating each operation to the client.</summary>
    private sealed class NfsV3Mount(NfsV3Client client) : INfsMount
    {
        public async Task<IReadOnlyList<NfsRemoteEntry>> ListAsync(string path, CancellationToken cancellationToken)
        {
            var entries = await client.ReadDirPlusAsync(path, cancellationToken);
            return [.. entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) && entry.Name is not ("." or ".."))
                .Select(entry =>
                {
                    var attributes = entry.Attr;
                    return new NfsRemoteEntry(entry.Name, attributes?.Type == NfsType.Dir, attributes?.Size ?? 0, attributes?.Mtime);
                })];
        }

        public async Task<NfsRemoteKind> KindAsync(string path, CancellationToken cancellationToken)
        {
            if (await client.IsDirectoryAsync(path, cancellationToken))
            {
                return NfsRemoteKind.Folder;
            }

            return await client.FileExistsAsync(path, cancellationToken) ? NfsRemoteKind.File : NfsRemoteKind.None;
        }

        public Task ReadAsync(string path, Stream destination, CancellationToken cancellationToken) =>
            client.ReadFileAsync(path, destination, cancellationToken);

        public async Task WriteAsync(string path, Stream source, CancellationToken cancellationToken) =>
            _ = await client.WriteFileAsync(path, source, cancellationToken);

        public async Task CreateFolderAsync(string path, CancellationToken cancellationToken) =>
            _ = await client.CreateDirectoryAsync(path, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken) =>
            client.DeleteFileAsync(path, cancellationToken);

        public Task DeleteFolderAsync(string path, bool recursive, CancellationToken cancellationToken) =>
            client.DeleteDirectoryAsync(path, recursive, cancellationToken);

        public Task MoveAsync(string from, string to, CancellationToken cancellationToken) =>
            client.MoveAsync(from, to, cancellationToken);

        public async ValueTask DisposeAsync() => await client.DisposeAsync();
    }
}
