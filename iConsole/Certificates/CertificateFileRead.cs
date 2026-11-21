namespace iConsole.Certificates;

/// <summary>The bytes of a certificate file, or why they could not be read.</summary>
public sealed record CertificateFileRead(CertificateFileStatus Status, byte[] Bytes, string? Error = null);
