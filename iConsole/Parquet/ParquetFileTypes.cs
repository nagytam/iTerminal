namespace iConsole.Parquet;

/// <summary>Which files the Parquet viewer opens: the endings, and the magic bytes that tell a Parquet file from anything else with the same ending.</summary>
public static class ParquetFileTypes
{
    /// <summary>The endings the Parquet viewer opens.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".parquet", ".parq" };

    /// <summary>The 4 bytes every Parquet file starts with. An encrypted Parquet file starts with PARE instead, and is read by the generic viewer.</summary>
    public static readonly byte[] Signature = [(byte)'P', (byte)'A', (byte)'R', (byte)'1'];

    public static bool IsParquetFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    /// <summary>True when the bytes begin with the Parquet magic. A file shorter than the magic, such as an empty one, is not Parquet.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length && bytes[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Reads the first bytes of the file and checks the magic. Blocking I/O: call it from a worker thread. A file that cannot be
    /// opened or read is not Parquet as far as the viewer is concerned, so the generic viewer reports the error as it always did.
    /// </summary>
    public static bool FileHasSignature(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] buffer = new byte[Signature.Length];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return HasSignature(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
