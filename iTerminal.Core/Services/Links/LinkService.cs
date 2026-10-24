using System.ComponentModel;
using System.Runtime.InteropServices;

namespace iTerminal.Core.Services.Links;

public enum LinkKind
{
    /// <summary>A small file or folder entry that names another path; it can cross volumes and can dangle.</summary>
    Symbolic,

    /// <summary>A second name for the same file on the same volume; neither name is "the" one.</summary>
    Hard,
}

/// <summary>
/// Creates links on the local disk. Async by contract — a disk operation never runs on a key
/// handler — and a failure comes back as an <see cref="IOException"/> whose message is fit to be
/// shown as it is.
/// </summary>
public interface ILinkService
{
    /// <param name="linkPath">The link to create; it must not exist.</param>
    /// <param name="targetPath">The existing file or folder it leads to.</param>
    /// <param name="relative">
    /// For a symbolic link: write the target relative to the link's folder, so the pair can be
    /// moved or copied together and still work. Ignored for a hard link, which holds no path.
    /// </param>
    Task CreateAsync(LinkKind kind, string linkPath, string targetPath, bool relative = false, CancellationToken cancellationToken = default);

    /// <summary>What <paramref name="path"/> is as a link: where a symbolic link points, or how many names a file has.</summary>
    Task<LinkInfo> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the existing link (or, for <see cref="LinkKind.Hard"/>, one of the names of a file that
    /// has several) lead somewhere else, or become the other kind of link. A failure leaves the
    /// original as it was.
    /// </summary>
    Task ReplaceAsync(LinkKind kind, string linkPath, string targetPath, bool relative = false, CancellationToken cancellationToken = default);
}

/// <summary>What the disk says about a path, as far as links go.</summary>
/// <param name="SymbolicTarget">Where a symbolic link points, as written in it (maybe relative, maybe dangling); null when it is not one.</param>
/// <param name="NameCount">How many names the file has; 1 for a folder, a symbolic link, or when the disk would not say.</param>
public readonly record struct LinkInfo(string? SymbolicTarget, int NameCount, bool IsFolder)
{
    public bool IsSymbolic => SymbolicTarget is not null;

    /// <summary>A file that another name also leads to: its name can be pointed elsewhere without losing the data.</summary>
    public bool IsHard => !IsSymbolic && !IsFolder && NameCount > 1;

    public bool IsEditable => IsSymbolic || IsHard;
}

public class LinkService : ILinkService
{
    public Task CreateAsync(LinkKind kind, string linkPath, string targetPath, bool relative = false, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(kind, linkPath, targetPath, relative), cancellationToken);

    public Task<LinkInfo> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Inspect(path), cancellationToken);

    public Task ReplaceAsync(LinkKind kind, string linkPath, string targetPath, bool relative = false, CancellationToken cancellationToken = default) =>
        Task.Run(() => Replace(kind, linkPath, targetPath, relative), cancellationToken);

    internal static LinkInfo Inspect(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var symbolicTarget = iConsole.Helper.FileLinkHelper.GetLinkTarget(path);
        if (symbolicTarget is not null)
        {
            return new LinkInfo(symbolicTarget, 1, Directory.Exists(path));
        }

        if (Directory.Exists(path))
        {
            return new LinkInfo(null, 1, true);
        }

        return new LinkInfo(null, File.Exists(path) ? GetNameCount(path) : 1, false);
    }

    internal static void Replace(LinkKind kind, string linkPath, string targetPath, bool relative)
    {
        linkPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkPath));
        targetPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));

        var info = Inspect(linkPath);
        if (!info.IsEditable)
        {
            throw new IOException($"{linkPath} is not a link, and has no other name.");
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(linkPath, targetPath, comparison))
        {
            throw new IOException("A link cannot lead to itself.");
        }

        var targetIsFolder = Directory.Exists(targetPath);
        if (!targetIsFolder && !File.Exists(targetPath))
        {
            throw new IOException($"{targetPath} does not exist.");
        }

        if (kind == LinkKind.Hard && targetIsFolder)
        {
            throw new IOException("A hard link can only be made to a file. Make a symbolic link to a folder.");
        }

        if (info.IsSymbolic && info.IsFolder)
        {
            // A link to a folder cannot be renamed over: take it away, make the new one, and put the
            // old one back if that fails.
            Directory.Delete(linkPath);
            try
            {
                Create(kind, linkPath, targetPath, relative);
            }
            catch
            {
                Directory.CreateSymbolicLink(linkPath, info.SymbolicTarget!);
                throw;
            }

            return;
        }

        // A file name is replaced in one step, so there is never a moment with no name at all: the
        // new link is made beside it, under a name of its own, and renamed over the old one.
        var folder = Path.GetDirectoryName(linkPath)!;
        var temporary = Path.Combine(folder, $".{Path.GetFileName(linkPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Create(kind, temporary, targetPath, relative);
            File.Move(temporary, linkPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary) || iConsole.Helper.FileLinkHelper.IsLink(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>1 when the disk would not say — which only withholds an offer, it never loses data.</summary>
    internal static int GetNameCount(string path)
    {
        try
        {
            return OperatingSystem.IsWindows() ? GetNameCountWindows(path) : GetNameCountUnix(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            return 1;
        }
    }

    private static int GetNameCountWindows(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return GetFileInformationByHandle(handle, out var information) ? (int)information.NumberOfLinks : 1;
    }

    private static int GetNameCountUnix(string path)
    {
        // struct stat is laid out per architecture; st_nlink is read where it is on the two that matter.
        var buffer = new byte[512];
        if (stat(path, buffer) != 0)
        {
            return 1;
        }

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => (int)BitConverter.ToUInt64(buffer, 16),
            Architecture.Arm64 => (int)BitConverter.ToUInt32(buffer, 20),
            _ => 1,
        };
    }

    internal static void Create(LinkKind kind, string linkPath, string targetPath, bool relative)
    {
        linkPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkPath));
        targetPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));

        var targetIsFolder = Directory.Exists(targetPath);
        if (!targetIsFolder && !File.Exists(targetPath))
        {
            throw new IOException($"{targetPath} does not exist.");
        }

        // Asked of the path itself, link or not: a link whose target is gone exists to neither
        // File.Exists nor Directory.Exists on Linux, and is still in the way.
        if (File.Exists(linkPath) || Directory.Exists(linkPath) || iConsole.Helper.FileLinkHelper.IsLink(linkPath))
        {
            throw new IOException($"{linkPath} already exists.");
        }

        var folder = Path.GetDirectoryName(linkPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            throw new IOException($"The folder {folder} does not exist.");
        }

        if (kind == LinkKind.Hard)
        {
            if (targetIsFolder)
            {
                throw new IOException("A hard link can only be made to a file. Make a symbolic link to a folder.");
            }

            CreateHardLink(linkPath, targetPath);
            return;
        }

        var writtenTarget = relative ? Path.GetRelativePath(folder, targetPath) : targetPath;
        try
        {
            if (targetIsFolder)
            {
                Directory.CreateSymbolicLink(linkPath, writtenTarget);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, writtenTarget);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(DescribeSymbolicLinkFailure(exception, targetIsFolder), exception);
        }
    }

    /// <summary>
    /// Windows refuses symbolic links to an ordinary user unless Developer Mode is on, and says so
    /// as "A required privilege is not held by the client" — true, and no help. This says what to
    /// do about it, and that a hard link needs no such thing.
    /// </summary>
    internal static string DescribeSymbolicLinkFailure(Exception exception, bool targetIsFolder)
    {
        const int ERROR_PRIVILEGE_NOT_HELD = 1314;
        var isPrivilege = OperatingSystem.IsWindows()
            && ((exception.HResult & 0xFFFF) == ERROR_PRIVILEGE_NOT_HELD || exception is UnauthorizedAccessException);
        if (!isPrivilege)
        {
            return exception.Message;
        }

        return "Windows only lets this account create symbolic links with Developer Mode on (Settings › System › For developers) or from an elevated terminal."
            + (targetIsFolder ? string.Empty : " A hard link needs neither.");
    }

    private static void CreateHardLink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(linkPath, targetPath, IntPtr.Zero))
            {
                throw new IOException(DescribeHardLinkFailure(Marshal.GetLastPInvokeError()));
            }
        }
        else if (link(targetPath, linkPath) != 0)
        {
            throw new IOException(DescribeHardLinkFailure(Marshal.GetLastPInvokeError()));
        }
    }

    internal static string DescribeHardLinkFailure(int error)
    {
        const int ERROR_NOT_SAME_DEVICE = 17;
        const int EXDEV = 18;
        var crossesVolumes = OperatingSystem.IsWindows() ? error == ERROR_NOT_SAME_DEVICE : error == EXDEV;
        return crossesVolumes
            ? "A hard link has to be on the same volume as the file. Make a symbolic link instead."
            : new Win32Exception(error).Message;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    [DllImport("libc", SetLastError = true)]
    private static extern int stat(string path, byte[] buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
