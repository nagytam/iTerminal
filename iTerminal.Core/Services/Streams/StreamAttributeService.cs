using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using iConsole.Localization;
using Microsoft.Win32.SafeHandles;

namespace iTerminal.Core.Services.Streams;

/// <summary>
/// <see cref="IStreamAttributeService"/> over the platform's own calls.
///
/// <list type="bullet">
/// <item><b>Windows</b>: <c>FindFirstStreamW</c>/<c>FindNextStreamW</c> list the streams. A stream is read through
/// <c>CreateFileW</c> on <c>path:name</c> with backup semantics (a folder cannot be opened without them) and
/// <c>FILE_FLAG_OPEN_REPARSE_POINT</c>, and deleted with <c>DeleteFileW</c>. The unnamed <c>::$DATA</c> stream is the file
/// itself and is not listed; the folder-only <c>$INDEX_ALLOCATION</c> streams are skipped.</item>
/// <item><b>Linux and macOS</b>: <c>listxattr</c>, <c>getxattr</c>, <c>removexattr</c>. The two systems' signatures differ
/// (macOS takes an options argument, and a position for <c>getxattr</c>), so each has its own import. The link's own variants
/// are used (<c>l</c> calls on Linux, <c>XATTR_NOFOLLOW</c> on macOS): a symbolic link shows its own attributes.</item>
/// <item><b>A name is checked before it reaches the system</b>: on Windows it must be a plain file name (no <c>:</c>, separator
/// or wildcard), so it cannot address another file; on Unix any non-empty name without NUL is a legal attribute name.</item>
/// <item>An attribute the caller may not read (<c>security.*</c>, <c>trusted.*</c> without root) is listed with size 0 and
/// refused with the system's own sentence when read or removed.</item>
/// </list>
/// </summary>
public class StreamAttributeService : IStreamAttributeService
{
    private const int MaxReadableBytes = 4 * 1024 * 1024;

    public string Kind => OperatingSystem.IsWindows()
        ? UiText.Get("Streams.Kind.Streams", "Streams")
        : UiText.Get("Streams.Kind.Attributes", "Attributes");

    public Task<StreamListing> ListAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Guard(() => List(path), failure => new StreamListing([], failure)), cancellationToken);

    public Task<StreamData> ReadAsync(string path, string name, int maxBytes, CancellationToken cancellationToken = default) =>
        Task.Run(() => Guard(() => Read(path, name, Math.Max(1, maxBytes)), failure => new StreamData([], 0, failure)), cancellationToken);

    public Task<StreamResult> DeleteAsync(string path, string name, CancellationToken cancellationToken = default) =>
        Task.Run(() => Guard(() => Delete(path, name), failure => new StreamResult(false, failure)), cancellationToken);

    private static T Guard<T>(Func<T> work, Func<string, T> onFailure)
    {
        try
        {
            return work();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return onFailure(exception.Message);
        }
    }

    /// <summary>Whether <paramref name="name"/> may be handed to the system as a stream or attribute name.</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255 || name.Contains('\0'))
        {
            return false;
        }

        return !OperatingSystem.IsWindows() || (name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not ("." or ".."));
    }

    private static string BadName(string name) =>
        string.Format(UiText.Get("Streams.Error.BadName", "'{0}' is not a valid name."), name);

    private static string Unsupported() =>
        UiText.Get("Streams.Error.Unsupported", "This system has no streams or extended attributes.");

    private static bool IsUnix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static StreamListing List(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new StreamListing(WindowsNative.List(path));
        }

        return IsUnix ? new StreamListing(UnixNative.List(path)) : new StreamListing([], Unsupported());
    }

    private static StreamData Read(string path, string name, int maxBytes)
    {
        if (!IsValidName(name))
        {
            return new StreamData([], 0, BadName(name));
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsNative.Read(path, name, maxBytes);
        }

        return IsUnix ? UnixNative.Read(path, name, maxBytes) : new StreamData([], 0, Unsupported());
    }

    private static StreamResult Delete(string path, string name)
    {
        if (!IsValidName(name))
        {
            return new StreamResult(false, BadName(name));
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsNative.Remove(path, name);
        }
        else if (IsUnix)
        {
            UnixNative.Remove(path, name);
        }
        else
        {
            return new StreamResult(false, Unsupported());
        }

        return new StreamResult(true, string.Format(UiText.Get("Streams.Deleted", "Deleted {0}."), name));
    }

    /// <summary>Splits the NUL-separated name list <c>listxattr</c> fills in.</summary>
    public static IReadOnlyList<string> ParseAttributeNames(ReadOnlySpan<byte> buffer)
    {
        List<string> names = [];
        int start = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == 0)
            {
                if (i > start)
                {
                    names.Add(Encoding.UTF8.GetString(buffer[start..i]));
                }

                start = i + 1;
            }
        }

        return names;
    }

    /// <summary>Turns the name Windows reports (<c>:Zone.Identifier:$DATA</c>) into the stream's own; null for the main stream and for streams that are not data.</summary>
    public static string? ParseWindowsStreamName(string reported)
    {
        const string Suffix = ":$DATA";
        if (!reported.StartsWith(':') || !reported.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = reported[1..^Suffix.Length];
        return name.Length == 0 ? null : name;
    }

    private static class WindowsNative
    {
        private const int ErrorHandleEof = 38;
        private const uint GenericRead = 0x80000000;
        private const uint ShareAll = 7;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;
        private const uint OpenReparsePoint = 0x00200000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindStreamData
        {
            public long StreamSize;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
            public string StreamName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out FindStreamData data, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextStreamW(IntPtr handle, out FindStreamData data);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileW(string fileName);

        private static string Own(string path) => path.Length > 3 ? path.TrimEnd('\\', '/') : path;

        private static string StreamPath(string path, string name) => Own(path) + ":" + name;

        public static IReadOnlyList<StreamItem> List(string path)
        {
            List<StreamItem> items = [];
            IntPtr handle = FindFirstStreamW(Own(path), 0, out FindStreamData data, 0);
            if (handle == new IntPtr(-1))
            {
                int error = Marshal.GetLastPInvokeError();
                return error == ErrorHandleEof ? items : throw new Win32Exception(error); // EOF: a folder with no named streams.
            }

            try
            {
                do
                {
                    if (ParseWindowsStreamName(data.StreamName) is { } name)
                    {
                        items.Add(new StreamItem(name, data.StreamSize));
                    }
                }
                while (FindNextStreamW(handle, out data));

                int end = Marshal.GetLastPInvokeError();
                if (end != ErrorHandleEof)
                {
                    throw new Win32Exception(end);
                }
            }
            finally
            {
                FindClose(handle);
            }

            return items;
        }

        public static StreamData Read(string path, string name, int maxBytes)
        {
            using SafeFileHandle handle = CreateFileW(StreamPath(path, name), GenericRead, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            using FileStream stream = new(handle, FileAccess.Read, 1);
            long total = stream.Length;
            byte[] buffer = new byte[(int)Math.Min(total, maxBytes)];
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            return new StreamData(filled == buffer.Length ? buffer : buffer[..filled], total);
        }

        public static void Remove(string path, string name)
        {
            if (!DeleteFileW(StreamPath(path, name)))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    private static class UnixNative
    {
        private const int XattrNoFollow = 1;
        private const int Erange = 34;

        [DllImport("libc", EntryPoint = "llistxattr", SetLastError = true)]
        private static extern nint LinuxList(string path, byte[]? list, nuint size);

        [DllImport("libc", EntryPoint = "lgetxattr", SetLastError = true)]
        private static extern nint LinuxGet(string path, string name, byte[]? value, nuint size);

        [DllImport("libc", EntryPoint = "lremovexattr", SetLastError = true)]
        private static extern int LinuxRemove(string path, string name);

        [DllImport("libc", EntryPoint = "listxattr", SetLastError = true)]
        private static extern nint MacList(string path, byte[]? list, nuint size, int options);

        [DllImport("libc", EntryPoint = "getxattr", SetLastError = true)]
        private static extern nint MacGet(string path, string name, byte[]? value, nuint size, uint position, int options);

        [DllImport("libc", EntryPoint = "removexattr", SetLastError = true)]
        private static extern int MacRemove(string path, string name, int options);

        private static nint ListCall(string path, byte[]? buffer) => OperatingSystem.IsMacOS()
            ? MacList(path, buffer, (nuint)(buffer?.Length ?? 0), XattrNoFollow)
            : LinuxList(path, buffer, (nuint)(buffer?.Length ?? 0));

        private static nint GetCall(string path, string name, byte[]? buffer) => OperatingSystem.IsMacOS()
            ? MacGet(path, name, buffer, (nuint)(buffer?.Length ?? 0), 0, XattrNoFollow)
            : LinuxGet(path, name, buffer, (nuint)(buffer?.Length ?? 0));

        private static Win32Exception Failure() => new(Marshal.GetLastPInvokeError());

        public static IReadOnlyList<StreamItem> List(string path)
        {
            // The list can grow between asking its size and reading it (ERANGE): ask again.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                nint size = ListCall(path, null);
                if (size < 0)
                {
                    throw Failure();
                }

                if (size == 0)
                {
                    return [];
                }

                byte[] buffer = new byte[size];
                nint got = ListCall(path, buffer);
                if (got < 0)
                {
                    if (Marshal.GetLastPInvokeError() == Erange)
                    {
                        continue;
                    }

                    throw Failure();
                }

                List<StreamItem> items = [];
                foreach (string name in ParseAttributeNames(buffer.AsSpan(0, (int)got)))
                {
                    // One that cannot be read (security.*, or removed meanwhile) is listed as empty rather than failing the list.
                    items.Add(new StreamItem(name, Math.Max(0, GetCall(path, name, null))));
                }

                return items;
            }

            throw new IOException(UiText.Get("Streams.Error.Changing", "The attributes keep changing; try again."));
        }

        public static StreamData Read(string path, string name, int maxBytes)
        {
            nint size = GetCall(path, name, null);
            if (size < 0)
            {
                throw Failure();
            }

            if (size > MaxReadableBytes)
            {
                return new StreamData([], size, UiText.Get("Streams.Error.TooLarge", "Too large to preview."));
            }

            byte[] buffer = new byte[size];
            nint got = size == 0 ? 0 : GetCall(path, name, buffer);
            if (got < 0)
            {
                throw Failure();
            }

            return new StreamData(buffer.AsSpan(0, (int)Math.Min(got, maxBytes)).ToArray(), got);
        }

        public static void Remove(string path, string name)
        {
            int result = OperatingSystem.IsMacOS() ? MacRemove(path, name, XattrNoFollow) : LinuxRemove(path, name);
            if (result != 0)
            {
                throw Failure();
            }
        }
    }
}
