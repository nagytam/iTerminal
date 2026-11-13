namespace iConsole.Certificates;

/// <summary>The bounds that keep a hostile or huge file from using the viewer: one place, so the reader and the tests agree.</summary>
public static class CertificateLimits
{
    /// <summary>The largest file that is read as a certificate file. A bigger one falls back to the text viewer.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>The most certificates listed from one file.</summary>
    public const int MaxCertificates = 64;

    /// <summary>The most alternative names listed for one certificate.</summary>
    public const int MaxNames = 256;
}
