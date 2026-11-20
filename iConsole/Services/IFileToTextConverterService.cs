using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;

namespace iConsole.Services;

public interface IFileToTextConverterService
{
    Task ReadFileAsync(Url? url, IFileSystem fileSystem, ITextModel textModel);

    /// <summary>
    /// The disassembly of a native or .NET binary (<c>.dll</c>, <c>.exe</c>, <c>.so</c>) as lines of text. Kept apart from
    /// <see cref="ReadFileAsync"/> because it is CPU work that can take seconds: only the F3 viewer asks for it, in the
    /// background, while the preview and the editor keep showing the metadata page alone.
    /// </summary>
    Task<IReadOnlyList<string>> BuildDisassemblyAsync(Url? url, IFileSystem fileSystem, CancellationToken cancellationToken);

    /// <summary>
    /// The "Digital signatures" section of a signed PDF as lines of text; empty for any other file or a PDF without embedded
    /// signatures. Kept apart from <see cref="ReadFileAsync"/> for the same reason as the disassembly: only the F3 viewer asks
    /// for it, in the background, so the preview and the editor never carry it in their text.
    /// </summary>
    Task<IReadOnlyList<string>> BuildPdfSignatureLinesAsync(Url? url, IFileSystem fileSystem, CancellationToken cancellationToken);
}