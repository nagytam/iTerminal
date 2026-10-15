namespace iTerminal.Test;

/// <summary>
/// Reads a file the app may still be writing, for a wait loop. A copy holds the file open while it
/// writes, and a plain <see cref="File.ReadAllText(string)"/> then fails with a sharing violation
/// instead of waiting for the write to finish. Returns null while the file cannot be read yet.
/// </summary>
internal static class SharedFileReader
{
    public static string? ReadAllText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
