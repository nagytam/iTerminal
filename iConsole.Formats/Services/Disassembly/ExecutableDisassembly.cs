using iConsole.Localization;

namespace iConsole.Services.Disassembly;

/// <summary>
/// Disassembles a local executable for the executable analysis view. It reads the file's bytes and hands them to the same builder the
/// F3 text viewer uses, so both views show the same listing. Blocking file I/O: call it from a worker thread.
/// </summary>
public static class ExecutableDisassembly
{
    /// <summary>The largest file read whole for disassembly. A bigger one gets a note instead of being loaded into memory.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>
    /// The disassembly lines for the file at <paramref name="path"/>. Never runs the file. A cancelled token surfaces as
    /// <see cref="OperationCanceledException"/>; an unreadable file surfaces as the <see cref="IOException"/> it raised.
    /// </summary>
    public static IReadOnlyList<string> Build(string path, CancellationToken cancellationToken)
    {
        long length = new FileInfo(path).Length;
        if (length > MaxFileBytes)
        {
            return [string.Format(UiText.Get("Exe.Disassembly.TooLarge", "The file is larger than {0:N0} MB and is not disassembled"), MaxFileBytes / (1024 * 1024))];
        }

        cancellationToken.ThrowIfCancellationRequested();
        return DisassemblyBuilder.Build(File.ReadAllBytes(path), cancellationToken);
    }
}
