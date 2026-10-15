using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace iTerminal.Core.Services;

public class SmbFileSystemService : ISmbFileSystemService
{
    private readonly Func<ISMBClient> _clientFactory;
    private ISMBClient? _client;
    private ISMBFileStore? _fileStore;

    public bool IsReadOnly => false;
    public bool IsConnected => _client?.IsConnected ?? false;

    public SmbFileSystemService() : this(() => new SMB2Client())
    {
    }

    public SmbFileSystemService(Func<ISMBClient> clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public IEnumerable<string> ListShares(string host, string username, string password)
    {
        var client = _clientFactory();
        try
        {
            if (!client.Connect(host, SMBTransportType.DirectTCPTransport))
            {
                return [];
            }
            var loginStatus = client.Login(string.Empty, username ?? string.Empty, password ?? string.Empty);
            if (loginStatus != NTStatus.STATUS_SUCCESS)
            {
                return [];
            }
            var shares = client.ListShares(out var listStatus);
            return listStatus == NTStatus.STATUS_SUCCESS && shares is not null ? shares : [];
        }
        catch
        {
            return [];
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    public void Connect(string host, string share, string username, string password)
    {
        if (_client is null || !_client.IsConnected)
        {
            _client = _clientFactory();
            if (!_client.Connect(host, SMBTransportType.DirectTCPTransport))
            {
                throw new IOException($"Could not connect to {host}");
            }
            var loginStatus = _client.Login(string.Empty, username ?? string.Empty, password ?? string.Empty);
            if (loginStatus != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"SMB login to {host} failed: {loginStatus}");
            }
        }
        _fileStore = _client.TreeConnect(share, out var treeStatus);
        if (treeStatus != NTStatus.STATUS_SUCCESS || _fileStore is null)
        {
            throw new IOException($"Could not connect to share \\\\{host}\\{share}: {treeStatus}");
        }
    }

    public void Disconnect()
    {
        try { _fileStore?.Disconnect(); } catch { }
        try { _client?.Logoff(); } catch { }
        try { _client?.Disconnect(); } catch { }
        _fileStore = null;
        _client = null;
    }

    public IEnumerable<IEntry> List(Url url)
    {
        List<FileSystemEntry> entries = [];
        if (_fileStore is null)
        {
            return entries;
        }

        if (!string.IsNullOrEmpty(url.SharePath))
        {
            entries.Add(new FileSystemEntry("..", url.ParentUrl.FullUrl));
        }

        var directoryPath = ToSmbPath(url.SharePath);
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, directoryPath,
            AccessMask.GENERIC_READ, FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            return entries;
        }

        try
        {
            status = _fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> fileList, handle, "*", FileInformationClass.FileDirectoryInformation);
            if (status == NTStatus.STATUS_SUCCESS || status == NTStatus.STATUS_NO_MORE_FILES)
            {
                foreach (var item in fileList)
                {
                    if (item is not FileDirectoryInformation info)
                    {
                        continue;
                    }
                    if (info.FileName is "." or "..")
                    {
                        continue;
                    }

                    var isFolder = info.FileAttributes.HasFlag(FileAttributes.Directory);
                    var childUrl = new Url(url.FullUrl.TrimEnd('/') + "/" + info.FileName + (isFolder ? "/" : string.Empty));
                    var entry = new FileSystemEntry(childUrl)
                    {
                        FileSize = isFolder ? Url.Folder : info.EndOfFile.ToFileSize(),
                        Date = info.LastWriteTime.ToString("yyyy-MM-dd"),
                        Time = info.LastWriteTime.ToString("HH:mm"),
                    };
                    entries.Add(entry);
                }
            }
        }
        finally
        {
            _fileStore.CloseFile(handle);
        }

        return entries;
    }

    public bool ExistsFile(Url url) => Exists(url, isFolder: false);

    public bool ExistsFolder(Url url) => Exists(url, isFolder: true);

    private bool Exists(Url url, bool isFolder)
    {
        if (_fileStore is null)
        {
            return false;
        }
        var path = ToSmbPath(url.SharePath);
        var createOptions = isFolder ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE;
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
            AccessMask.GENERIC_READ, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            createOptions, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            return false;
        }
        _fileStore.CloseFile(handle);
        return true;
    }

    public Task CreateFolderAsync(Url url)
    {
        if (_fileStore is not null)
        {
            var path = ToSmbPath(url.SharePath);
            var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
                AccessMask.GENERIC_READ, FileAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_CREATE,
                CreateOptions.FILE_DIRECTORY_FILE, null);
            if (status == NTStatus.STATUS_SUCCESS)
            {
                _fileStore.CloseFile(handle);
            }
        }
        return Task.CompletedTask;
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin) => Delete(url, isFolder: false);

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        if (recursive)
        {
            foreach (var entry in List(url))
            {
                if (entry.DisplayName == "..")
                {
                    continue;
                }
                if (entry.Url.PathType == PathType.Folder)
                {
                    DeleteFolder(entry.Url, recursive: true, moveToRecycleBin);
                }
                else
                {
                    DeleteFile(entry.Url, moveToRecycleBin);
                }
            }
        }
        return Delete(url, isFolder: true);
    }

    private bool Delete(Url url, bool isFolder)
    {
        if (_fileStore is null)
        {
            return false;
        }
        var path = ToSmbPath(url.SharePath);
        var createOptions = isFolder ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE;
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
            AccessMask.DELETE, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete, CreateDisposition.FILE_OPEN,
            createOptions, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            return false;
        }
        status = _fileStore.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
        _fileStore.CloseFile(handle);
        return status == NTStatus.STATUS_SUCCESS;
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (_fileStore is null)
        {
            return Task.CompletedTask;
        }
        var sourcePath = ToSmbPath(sourceUrl.SharePath);
        var targetPath = ToSmbPath(targetUrl.SharePath);
        var isFolder = sourceUrl.PathType == PathType.Folder;
        var createOptions = isFolder ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE;
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, sourcePath,
            AccessMask.DELETE | AccessMask.GENERIC_READ, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete, CreateDisposition.FILE_OPEN,
            createOptions, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new IOException($"SMB move failed: could not open {sourcePath} ({status})");
        }
        try
        {
            var renameInfo = new FileRenameInformationType2
            {
                FileName = targetPath,
                ReplaceIfExists = overwrite,
            };
            status = _fileStore.SetFileInformation(handle, renameInfo);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"SMB move failed: {status}");
            }
        }
        finally
        {
            _fileStore.CloseFile(handle);
        }
        return Task.CompletedTask;
    }

    public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (_fileStore is null)
        {
            return;
        }
        if (sourceUrl.PathType == PathType.Folder)
        {
            await CreateFolderAsync(targetUrl);
            foreach (var entry in List(sourceUrl))
            {
                if (entry.DisplayName == "..")
                {
                    continue;
                }
                var isChildFolder = entry.Url.PathType == PathType.Folder;
                var childTargetUrl = new Url(targetUrl.FullUrl.TrimEnd('/') + "/" + entry.Name + (isChildFolder ? "/" : string.Empty));
                await CopyAsync(entry.Url, childTargetUrl, overwrite);
            }
        }
        else
        {
            var file = await ReadFileAsync(sourceUrl);
            if (file?.Content is not null)
            {
                using var stream = new MemoryStream(file.Content);
                await WriteFileAsync(targetUrl, stream);
            }
        }
    }

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        if (_fileStore is null || _client is null)
        {
            return Task.FromResult<IMemoryFile?>(null);
        }
        var path = ToSmbPath(url.SharePath);
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
            AccessMask.GENERIC_READ, FileAttributes.Normal,
            ShareAccess.Read, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            return Task.FromResult<IMemoryFile?>(null);
        }

        try
        {
            using var memoryStream = new MemoryStream();
            long offset = 0;
            while (true)
            {
                status = _fileStore.ReadFile(out byte[] data, handle, offset, (int)_client.MaxReadSize);
                if (status == NTStatus.STATUS_END_OF_FILE || data is null || data.Length == 0)
                {
                    break;
                }
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"SMB read failed: {status}");
                }
                memoryStream.Write(data, 0, data.Length);
                offset += data.Length;
            }
            var memoryFile = new MemoryFile(url, memoryStream.ToArray());
            return Task.FromResult<IMemoryFile?>(memoryFile);
        }
        finally
        {
            _fileStore.CloseFile(handle);
        }
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        if (_fileStore is null || _client is null)
        {
            return Task.CompletedTask;
        }
        var path = ToSmbPath(url.SharePath);
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
            AccessMask.GENERIC_WRITE, FileAttributes.Normal,
            ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new IOException($"SMB write failed: could not create {path} ({status})");
        }

        try
        {
            var data = inputStream.ToArray();
            var maxWriteSize = (int)_client.MaxWriteSize;
            long offset = 0;
            while (offset < data.Length)
            {
                var count = Math.Min(maxWriteSize, data.Length - (int)offset);
                var chunk = new byte[count];
                Array.Copy(data, offset, chunk, 0, count);
                status = _fileStore.WriteFile(out int _, handle, offset, chunk);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"SMB write failed: {status}");
                }
                offset += count;
            }
            if (data.Length == 0)
            {
                _fileStore.WriteFile(out int _, handle, 0, []);
            }
        }
        finally
        {
            _fileStore.CloseFile(handle);
        }
        return Task.CompletedTask;
    }

    public string GetAvailableDiskSpace()
    {
        if (_fileStore is null)
        {
            return string.Empty;
        }
        try
        {
            var status = _fileStore.GetFileSystemInformation(out FileSystemInformation info, FileSystemInformationClass.FileFsFullSizeInformation);
            if (status == NTStatus.STATUS_SUCCESS && info is FileFsFullSizeInformation sizeInfo)
            {
                var availableBytes = sizeInfo.CallerAvailableAllocationUnits * sizeInfo.SectorsPerAllocationUnit * sizeInfo.BytesPerSector;
                return availableBytes.ToFileSize();
            }
        }
        catch
        {
        }
        return string.Empty;
    }

    private static string ToSmbPath(string sharePath)
    {
        return sharePath.Trim('/').Replace('/', '\\');
    }

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (_fileStore is null || _client is null)
        {
            throw new IOException("Not connected to an SMB share.");
        }
        var path = ToSmbPath(url.SharePath);
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
            AccessMask.GENERIC_READ, FileAttributes.Normal,
            ShareAccess.Read, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new IOException($"SMB read failed: could not open {path} ({status})");
        }

        try
        {
            long offset = 0;
            long totalRead = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                status = _fileStore.ReadFile(out byte[] data, handle, offset, (int)_client.MaxReadSize);
                if (status == NTStatus.STATUS_END_OF_FILE || data is null || data.Length == 0)
                {
                    break;
                }
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"SMB read failed: {status}");
                }
                await destination.WriteAsync(data, cancellationToken);
                offset += data.Length;
                totalRead += data.Length;
                progress?.Report(totalRead);
            }
        }
        finally
        {
            _fileStore.CloseFile(handle);
        }
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (_fileStore is null || _client is null)
        {
            throw new IOException("Not connected to an SMB share.");
        }
        var path = ToSmbPath(url.SharePath);
        var status = _fileStore.CreateFile(out object handle, out FileStatus _, path,
            AccessMask.GENERIC_WRITE, FileAttributes.Normal,
            ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new IOException($"SMB write failed: could not create {path} ({status})");
        }

        try
        {
            var maxWriteSize = (int)_client.MaxWriteSize;
            var buffer = new byte[maxWriteSize];
            long offset = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, maxWriteSize), cancellationToken)) > 0)
            {
                var chunk = bytesRead == buffer.Length ? buffer : buffer[..bytesRead];
                status = _fileStore.WriteFile(out int _, handle, offset, chunk);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"SMB write failed: {status}");
                }
                offset += bytesRead;
                progress?.Report(offset);
            }
            if (offset == 0)
            {
                _fileStore.WriteFile(out int _, handle, 0, []);
            }
        }
        finally
        {
            _fileStore.CloseFile(handle);
        }
    }
}
