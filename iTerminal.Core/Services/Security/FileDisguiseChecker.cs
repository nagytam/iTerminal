using iConsole.Security;

namespace iTerminal.Core.Services.Security;

/// <summary>
/// Reads the first bytes of a local file and records the kind they show in <see cref="FileDisguiseCache"/>.
/// This is I/O: call it on a background task, never from a key handler or Draw.
/// </summary>
public class FileDisguiseChecker
{
    public async Task<FileKind> DetectAsync(string path, CancellationToken cancellationToken = default)
    {
        FileKind kind = FileKind.Unknown;
        try
        {
            byte[] header = new byte[FileDisguise.HeaderLength];
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous);
            int read = 0;
            while (read < header.Length)
            {
                int count = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            kind = FileDisguise.Detect(header.AsSpan(0, read));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Unreadable: nothing to say about it. Not cached, so a later look tries again.
            return FileKind.Unknown;
        }

        FileDisguiseCache.Set(path, kind);
        return kind;
    }
}
