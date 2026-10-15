namespace iTerminal.Core.Services.Clipboard;

/// <summary>Files on the system clipboard: their local paths and whether the paster should move them.</summary>
public sealed record FileClipboardContent(IReadOnlyList<string> Paths, bool IsCut);

/// <summary>
/// The system clipboard as far as files go — what lets a copy made here be pasted in Explorer, a
/// mail client or a browser's upload dialog, and the other way round. Paths are local paths: a file
/// that is not on the local disk has nothing another program could open.
///
/// <para>Async by contract: the clipboard is shared with every other program, and a process that
/// owns it can keep it busy for a moment, which a key handler must not wait for.</para>
/// </summary>
public interface IFileClipboardService
{
    /// <summary>Puts <paramref name="paths"/> on the clipboard; <paramref name="isCut"/> marks them to be moved.</summary>
    Task SetFilesAsync(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken = default);

    /// <summary>The files on the clipboard, or null when it holds none (text, an image, nothing).</summary>
    Task<FileClipboardContent?> GetFilesAsync(CancellationToken cancellationToken = default);
}
