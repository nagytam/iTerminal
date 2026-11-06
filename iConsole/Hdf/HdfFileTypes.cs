namespace iConsole.Hdf;

/// <summary>Which files the HDF5 viewer opens: the endings, and the signature that tells an HDF5 file from anything else with the same ending.</summary>
public static class HdfFileTypes
{
    /// <summary>The endings the HDF5 viewer opens. A .hdf file is often HDF4, which is a different format, so the signature is checked too.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".h5", ".hdf5", ".hdf", ".he5" };

    /// <summary>The 8 bytes every HDF5 superblock starts with: 0x89 "HDF" CR LF Ctrl-Z LF.</summary>
    public static readonly byte[] Signature = [0x89, 0x48, 0x44, 0x46, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsHdfFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    /// <summary>True when the bytes begin with the HDF5 signature. A file shorter than the signature, such as an empty one, is not HDF5.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length && bytes[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Looks for the signature where the format allows it: at the start of the file, or after a user block, at 512 bytes
    /// and then at every doubling (1024, 2048, ...). Blocking I/O: call it from a worker thread.
    /// </summary>
    public static bool FileHasSignature(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] buffer = new byte[Signature.Length];
        long offset = 0;
        while (offset + Signature.Length <= stream.Length)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (HasSignature(buffer.AsSpan(0, read)))
            {
                return true;
            }

            offset = offset == 0 ? 512 : offset * 2;
        }

        return false;
    }
}
