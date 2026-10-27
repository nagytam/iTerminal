using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using iConsole.Services;
using iTerminal.Core.Services.Processes;
using Microsoft.Extensions.Hosting;

namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// Text on the Windows clipboard, as <c>CF_UNICODETEXT</c> with the CR LF line breaks Windows programs expect. Plain
/// Win32, like <see cref="WindowsFileClipboardService"/>: no OLE and no STA thread, so it runs on any pool thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTextClipboardService : ITextClipboardService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    /// <summary>How long to keep trying for a clipboard another program has open.</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(1);

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        Task.Run(() => Set(text, cancellationToken), cancellationToken);

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Get(cancellationToken), cancellationToken);

    /// <summary>The text as Windows holds it: every line break a CR LF, and a terminating NUL.</summary>
    internal static byte[] ToClipboardBytes(string? text) =>
        Encoding.Unicode.GetBytes((text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n") + "\0");

    /// <summary>The text from a clipboard block: up to the first NUL, with a single line feed for every line break.</summary>
    internal static string FromClipboardBytes(byte[] block)
    {
        string text = Encoding.Unicode.GetString(block, 0, block.Length & ~1);
        int end = text.IndexOf('\0');
        return (end < 0 ? text : text[..end]).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static void Set(string text, CancellationToken cancellationToken)
    {
        byte[] bytes = ToClipboardBytes(text);
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

            // Ownership of the handle passes to the system on success; it is freed here only on failure.
            if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
            {
                throw new IOException("The text could not be put on the clipboard.");
            }

            handle = IntPtr.Zero;
        }
        finally
        {
            if (opened)
            {
                CloseClipboard();
            }

            if (handle != IntPtr.Zero)
            {
                GlobalFree(handle);
            }
        }
    }

    private static string? Get(CancellationToken cancellationToken)
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText) || !Open(cancellationToken))
        {
            return null;
        }

        try
        {
            IntPtr handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            UIntPtr size = GlobalSize(handle);
            IntPtr pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                byte[] block = new byte[(int)Math.Min((ulong)size, int.MaxValue)];
                Marshal.Copy(pointer, block, 0, block.Length);
                return FromClipboardBytes(block);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool Open(CancellationToken cancellationToken)
    {
        // A window of this process owns the clipboard: with a null owner EmptyClipboard succeeds and SetClipboardData
        // then fails. The console window is ours; the desktop is the fallback.
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

/// <summary>
/// Text on the Linux or macOS clipboard through the tool that is there: <c>wl-copy</c>/<c>wl-paste</c> (Wayland),
/// <c>xclip</c> (X11) or <c>pbcopy</c>/<c>pbpaste</c> (macOS). With none installed the calls fail and the fallback
/// clipboard answers instead.
/// </summary>
public sealed class ProcessTextClipboardService(IExternalProcessRunner processRunner) : ITextClipboardService
{
    internal static IEnumerable<(string Program, string[] Arguments)> WriteCommands()
    {
        yield return ("wl-copy", ["--type", "text/plain"]);
        yield return ("xclip", ["-selection", "clipboard", "-i"]);
        yield return ("pbcopy", []);
    }

    internal static IEnumerable<(string Program, string[] Arguments)> ReadCommands()
    {
        yield return ("wl-paste", ["--no-newline", "--type", "text/plain"]);
        yield return ("xclip", ["-selection", "clipboard", "-o"]);
        yield return ("pbpaste", []);
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string error = "No clipboard tool (wl-copy, xclip or pbcopy) was found.";
        foreach ((string program, string[] arguments) in WriteCommands())
        {
            using MemoryStream sink = new();
            ProcessOutput output = await processRunner.RunToStreamAsync(
                program, arguments, sink, writeStandardInput: s => s.WriteAsync(bytes, cancellationToken).AsTask(), cancellationToken: cancellationToken);
            if (output.Success)
            {
                return;
            }

            error = output.FirstErrorLine;
        }

        throw new IOException(error);
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        foreach ((string program, string[] arguments) in ReadCommands())
        {
            ProcessOutput output = await processRunner.RunAsync(program, arguments, cancellationToken: cancellationToken);
            if (output.Success)
            {
                return output.StandardOutput.Replace("\r\n", "\n");
            }
        }

        return null;
    }
}

/// <summary>
/// The system clipboard when it works and an in-process one when it does not (no clipboard tool on this machine, a
/// clipboard another program will not let go of). Once the system clipboard has failed the in-process one answers for
/// the rest of the run, so a copy and the paste that follows it agree.
/// </summary>
public sealed class FallbackTextClipboardService(ITextClipboardService primary, ITextClipboardService fallback) : ITextClipboardService
{
    private volatile bool _primaryFailed;

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_primaryFailed)
        {
            try
            {
                await primary.SetTextAsync(text, cancellationToken);
                // Kept here too, so a later failure of the system clipboard still has the last copy to paste.
                await fallback.SetTextAsync(text, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _primaryFailed = true;
            }
        }

        await fallback.SetTextAsync(text, cancellationToken);
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        if (!_primaryFailed)
        {
            try
            {
                return await primary.GetTextAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _primaryFailed = true;
            }
        }

        return await fallback.GetTextAsync(cancellationToken);
    }
}

/// <summary>Hands the text controls the application's clipboard (<see cref="TextClipboard.Current"/>) once the host starts.</summary>
public sealed class TextClipboardHostedService(ITextClipboardService clipboard) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        TextClipboard.Current = clipboard;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
