using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// Files on the Windows clipboard, in the two formats Explorer itself uses: <c>CF_HDROP</c> (a
/// DROPFILES header and the wide, double-null-terminated paths) and "Preferred DropEffect" (1 copy,
/// 2 move — which is the whole difference between Ctrl+C and Ctrl+X). Plain Win32, no OLE, so it
/// needs no STA thread and runs on any pool thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileClipboardService : IFileClipboardService
{
    private const uint CfHdrop = 15;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;
    private const int DropFilesHeaderSize = 20;

    /// <summary>How long to keep trying for a clipboard another program has open.</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(2);

    public Task SetFilesAsync(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken = default) =>
        Task.Run(() => Set(paths, isCut, cancellationToken), cancellationToken);

    public Task<FileClipboardContent?> GetFilesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Get(cancellationToken), cancellationToken);

    private static void Set(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
        {
            return;
        }

        byte[] drop = BuildDropFiles(paths);
        IntPtr dropHandle = AllocateGlobal(drop);
        IntPtr effectHandle = AllocateGlobal(BitConverter.GetBytes(isCut ? DropEffectMove : DropEffectCopy));
        bool opened = false;
        try
        {
            opened = Open(cancellationToken);
            if (!opened)
            {
                throw new IOException("The clipboard is in use by another program.");
            }

            if (!EmptyClipboard())
            {
                throw new IOException("The clipboard could not be cleared.");
            }

            // Ownership of a handle passes to the system on success; it is freed here only on failure.
            if (SetClipboardData(CfHdrop, dropHandle) == IntPtr.Zero)
            {
                throw new IOException("The files could not be put on the clipboard.");
            }

            dropHandle = IntPtr.Zero;
            uint effectFormat = RegisterClipboardFormat("Preferred DropEffect");
            if (SetClipboardData(effectFormat, effectHandle) != IntPtr.Zero)
            {
                effectHandle = IntPtr.Zero;
            }
        }
        finally
        {
            if (opened)
            {
                CloseClipboard();
            }

            if (dropHandle != IntPtr.Zero)
            {
                GlobalFree(dropHandle);
            }

            if (effectHandle != IntPtr.Zero)
            {
                GlobalFree(effectHandle);
            }
        }
    }

    private static FileClipboardContent? Get(CancellationToken cancellationToken)
    {
        if (!IsClipboardFormatAvailable(CfHdrop))
        {
            return null;
        }

        if (!Open(cancellationToken))
        {
            return null;
        }

        try
        {
            IntPtr handle = GetClipboardData(CfHdrop);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            List<string> paths = ParseDropFiles(handle);
            if (paths.Count == 0)
            {
                return null;
            }

            bool isCut = false;
            uint effectFormat = RegisterClipboardFormat("Preferred DropEffect");
            if (IsClipboardFormatAvailable(effectFormat) && GetClipboardData(effectFormat) is { } effectHandle && effectHandle != IntPtr.Zero)
            {
                IntPtr pointer = GlobalLock(effectHandle);
                if (pointer != IntPtr.Zero)
                {
                    try
                    {
                        // Explorer sets COPY|LINK (5) for some sources: only a bare MOVE bit is a cut.
                        int effect = Marshal.ReadInt32(pointer);
                        isCut = (effect & DropEffectMove) != 0 && (effect & DropEffectCopy) == 0;
                    }
                    finally
                    {
                        GlobalUnlock(effectHandle);
                    }
                }
            }

            return new FileClipboardContent(paths, isCut);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>The DROPFILES header — offset to the names, a point, a non-client flag and "names are wide" — then the names.</summary>
    internal static byte[] BuildDropFiles(IReadOnlyList<string> paths)
    {
        StringBuilder names = new();
        foreach (string path in paths)
        {
            names.Append(path).Append('\0');
        }

        names.Append('\0');
        byte[] text = Encoding.Unicode.GetBytes(names.ToString());
        byte[] buffer = new byte[DropFilesHeaderSize + text.Length];
        BitConverter.GetBytes(DropFilesHeaderSize).CopyTo(buffer, 0);
        BitConverter.GetBytes(1).CopyTo(buffer, 16); // fWide
        text.CopyTo(buffer, DropFilesHeaderSize);
        return buffer;
    }

    /// <summary>Reads the names out of a DROPFILES block, wide or ANSI.</summary>
    private static List<string> ParseDropFiles(IntPtr handle)
    {
        List<string> paths = [];
        UIntPtr size = GlobalSize(handle);
        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return paths;
        }

        try
        {
            int length = (int)Math.Min((ulong)size, int.MaxValue);
            if (length <= DropFilesHeaderSize)
            {
                return paths;
            }

            byte[] block = new byte[length];
            Marshal.Copy(pointer, block, 0, length);
            return ParseDropFiles(block);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    internal static List<string> ParseDropFiles(byte[] block)
    {
        List<string> paths = [];
        if (block.Length <= DropFilesHeaderSize)
        {
            return paths;
        }

        int start = BitConverter.ToInt32(block, 0);
        bool wide = BitConverter.ToInt32(block, 16) != 0;
        if (start < DropFilesHeaderSize || start >= block.Length)
        {
            return paths;
        }

        string text = wide
            ? Encoding.Unicode.GetString(block, start, (block.Length - start) & ~1)
            : Encoding.Default.GetString(block, start, block.Length - start);
        foreach (string name in text.Split('\0'))
        {
            if (name.Length == 0)
            {
                break;
            }

            paths.Add(name);
        }

        return paths;
    }

    private static IntPtr AllocateGlobal(byte[] bytes)
    {
        IntPtr handle = GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new OutOfMemoryException("No memory for the clipboard.");
        }

        IntPtr pointer = GlobalLock(handle);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    /// <summary>Opens the clipboard, retrying while another program holds it.</summary>
    private static bool Open(CancellationToken cancellationToken)
    {
        // A window of this process owns the clipboard: with a null owner EmptyClipboard succeeds and
        // SetClipboardData then fails. The console window is ours; the desktop is the fallback.
        IntPtr owner = GetConsoleWindow();
        if (owner == IntPtr.Zero)
        {
            owner = GetDesktopWindow();
        }

        DateTime deadline = DateTime.UtcNow + OpenTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OpenClipboard(owner))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(20);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
