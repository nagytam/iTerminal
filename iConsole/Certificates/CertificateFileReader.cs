namespace iConsole.Certificates;

/// <summary>How reading a certificate file ended.</summary>
public enum CertificateFileStatus
{
    /// <summary>The whole file was read within the limit.</summary>
    Ok,

    /// <summary>The file is larger than <see cref="CertificateLimits.MaxFileBytes"/>; nothing past the limit was kept.</summary>
    Overflow,

    /// <summary>The file could not be read (missing, locked, a permission error).</summary>
    Failure,
}

/// <summary>The bytes of a certificate file, or why they could not be read.</summary>
public sealed record CertificateFileRead(CertificateFileStatus Status, byte[] Bytes, string? Error = null);

/// <summary>Reads a local certificate file with a hard size limit. It never throws and never reads more than the limit.</summary>
public static class CertificateFileReader
{
    public static CertificateFileRead ReadCapped(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[8192];
            // Never ask for more than the bytes left under the limit plus one, so the buffer cannot grow past that.
            while (true)
            {
                long room = CertificateLimits.MaxFileBytes + 1 - buffer.Length;
                if (room <= 0)
                {
                    return new CertificateFileRead(CertificateFileStatus.Overflow, []);
                }

                int read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, room));
                if (read == 0)
                {
                    return new CertificateFileRead(CertificateFileStatus.Ok, buffer.ToArray());
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new CertificateFileRead(CertificateFileStatus.Failure, [], exception.Message);
        }
    }
}
