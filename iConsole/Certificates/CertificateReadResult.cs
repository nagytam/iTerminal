namespace iConsole.Certificates;

/// <summary>
/// What a certificate file held. <see cref="Certificates"/> is empty when there was no certificate; the viewer then
/// falls back to the text viewer. <see cref="PrivateKeyBlocks"/> counts the key blocks that were skipped.
/// </summary>
public sealed record CertificateReadResult(
    IReadOnlyList<CertificateInfo> Certificates,
    int PrivateKeyBlocks,
    string? Error);
