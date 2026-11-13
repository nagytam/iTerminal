namespace iConsole.Certificates;

/// <summary>Whether a certificate is inside its validity period at a given moment.</summary>
public enum CertificateValidity
{
    Valid,
    Expired,
    NotYetValid,
}

/// <summary>
/// The fields of one certificate, as the viewer shows them. Strings are already free of control characters, so they
/// can be drawn as they are. The private key is never part of this record.
/// </summary>
public sealed record CertificateInfo(
    string Subject,
    string Issuer,
    string SerialNumber,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    string SignatureAlgorithm,
    string PublicKeyAlgorithm,
    int KeySize,
    IReadOnlyList<string> SubjectAlternativeNames,
    bool IsCertificateAuthority,
    int? PathLength,
    IReadOnlyList<string> KeyUsages,
    IReadOnlyList<string> EnhancedKeyUsages,
    string Sha256Fingerprint,
    string Sha1Fingerprint,
    bool SelfIssued)
{
    /// <summary>Where the certificate stands at <paramref name="now"/>.</summary>
    public CertificateValidity ValidityAt(DateTimeOffset now) =>
        now < NotBefore ? CertificateValidity.NotYetValid
        : now > NotAfter ? CertificateValidity.Expired
        : CertificateValidity.Valid;
}
