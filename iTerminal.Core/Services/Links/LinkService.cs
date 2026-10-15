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
}

public class LinkService : ILinkService
{
    public Task CreateAsync(LinkKind kind, string linkPath, string targetPath, bool relative = false, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(kind, linkPath, targetPath, relative), cancellationToken);

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
}
