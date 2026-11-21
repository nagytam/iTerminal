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
