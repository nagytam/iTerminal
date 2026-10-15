using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using Microsoft.VisualBasic.FileIO;

namespace iTerminal.Core.Services;

public class FileSystemService : IFileSystemService
{
    public bool IsReadOnly => false;

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        if (DeleteLink(url.FullName))
        {
            return true;
        }

        if (!File.Exists(url.FullName))
            return false;

        if (moveToRecycleBin && OperatingSystem.IsWindows())
        {
            FileSystem.DeleteFile(url.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            File.Delete(url.FullName);
        }
        return true;
    }
    /// <summary>
    /// Removes the link at <paramref name="path"/> — the link, never what it points to — and says
    /// whether there was one. Ahead of everything else a delete does: a link to a folder is a
    /// folder to <see cref="Directory.Exists"/>, and a recursive delete or a trip to the recycle
    /// bin must not be given the chance to treat it as the folder it leads to. A link whose target
    /// is gone exists to neither <see cref="File.Exists"/> nor <see cref="Directory.Exists"/> on
    /// Linux, and is still there to be removed.
    /// </summary>
    private static bool DeleteLink(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (!iConsole.Helper.FileLinkHelper.IsLink(trimmed))
        {
            return false;
        }

        if ((File.GetAttributes(trimmed) & FileAttributes.Directory) != 0)
        {
            Directory.Delete(trimmed, recursive: false);
        }
        else
        {
            File.Delete(trimmed);
        }

        return true;
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        if (DeleteLink(url.FullName))
        {
            return true;
        }

        if (!Directory.Exists(url.FullName))
            return false;

        if (moveToRecycleBin && OperatingSystem.IsWindows())
        {
            FileSystem.DeleteDirectory(url.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            Directory.Delete(url.FullName, recursive);
        }
        return true;
    }

    public string GetAvailableDiskSpace()
    {
        throw new NotImplementedException();
    }

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        var buffer = File.ReadAllBytes(url.FullName);
        var memoryFile = new MemoryFile(url, buffer);
        return Task.FromResult<IMemoryFile?>(memoryFile);
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        var buffer = inputStream.ToArray();
        File.WriteAllBytes(url.FullName, buffer);
        return Task.CompletedTask;
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        switch (sourceUrl.PathType)
        {
            case PathType.File:
                {
                    File.Move(sourceUrl.FullName, targetUrl.FullName, overwrite);
                    break;
                }
            case PathType.Folder:
                {
                    if (Directory.Exists(targetUrl.FullName))
                    {
                        if (!overwrite)
                        {
                            throw new IOException($"The folder '{targetUrl.FullName}' already exists.");
                        }
                        Directory.Delete(targetUrl.FullName, recursive: true);
                    }
                    Directory.Move(sourceUrl.FullName, targetUrl.FullName);
                    break;
                }
            default:
                {
                    throw new NotImplementedException();
                }
        }
        return Task.CompletedTask;
    }

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        switch (sourceUrl.PathType)
        {
            case PathType.File:
                {
                    File.Copy(sourceUrl.FullName, targetUrl.FullName, overwrite);
                    break;
                }
            case PathType.Folder:
                {
                    CopyFolderRecursive(sourceUrl.FullName, targetUrl.FullName, overwrite);
                    break;
                }
            default:
                {
                    throw new NotImplementedException();
                }
        }
        return Task.CompletedTask;
    }

    private static void CopyFolderRecursive(string sourceDir, string targetDir, bool overwrite)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite);
        }
        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            // A link back to a folder this one is inside: copying through it never ends.
            if (iConsole.Helper.FileLinkHelper.IsLink(subDir) && iConsole.Helper.FileLinkHelper.LeadsToAnAncestor(subDir))
            {
                continue;
            }

            var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyFolderRecursive(subDir, destSubDir, overwrite);
        }
    }

    public bool ExistsFile(Url url)
    {
        return File.Exists(url.Path); // TODO
    }

    public bool ExistsFolder(Url url)
    {
        return Directory.Exists(url.Path); // TODO
    }

    public Task CreateFolderAsync(Url url)
    {
        Directory.CreateDirectory(url.Path);
        return Task.CompletedTask;
    }

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var sourceStream = new FileStream(url.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, StreamCopyHelper.DefaultBufferSize, useAsync: true);
        await StreamCopyHelper.CopyWithProgressAsync(sourceStream, destination, progress, cancellationToken);
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var targetStream = new FileStream(url.FullName, FileMode.Create, FileAccess.Write, FileShare.None, StreamCopyHelper.DefaultBufferSize, useAsync: true);
        await StreamCopyHelper.CopyWithProgressAsync(source, targetStream, progress, cancellationToken);
    }
}
