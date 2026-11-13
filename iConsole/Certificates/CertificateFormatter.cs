using System.Globalization;
using iConsole.Localization;

namespace iConsole.Certificates;

/// <summary>The lines the viewer shows for one certificate, and its validity line. Pure: the clock is passed in.</summary>
public static class CertificateFormatter
{
    public static IReadOnlyList<string> Lines(CertificateInfo certificate)
    {
        const string none = "—";
        return
        [
            Field("Certificate.Field.Subject", "Subject", certificate.Subject),
            Field("Certificate.Field.Issuer", "Issuer", certificate.Issuer),
            Field("Certificate.Field.SelfIssued", "Self-issued", Yes(certificate.SelfIssued)),
            Field("Certificate.Field.Serial", "Serial number", certificate.SerialNumber),
            Field("Certificate.Field.NotBefore", "Valid from", Date(certificate.NotBefore)),
            Field("Certificate.Field.NotAfter", "Valid to", Date(certificate.NotAfter)),
            Field("Certificate.Field.SignatureAlgorithm", "Signature", certificate.SignatureAlgorithm),
            Field("Certificate.Field.PublicKey", "Public key", certificate.KeySize > 0
                ? $"{certificate.PublicKeyAlgorithm}, {certificate.KeySize} bit"
                : certificate.PublicKeyAlgorithm),
            Field("Certificate.Field.Names", "Alternative names", certificate.SubjectAlternativeNames.Count == 0 ? none : string.Join(", ", certificate.SubjectAlternativeNames)),
            Field("Certificate.Field.CertificateAuthority", "Certificate authority", Yes(certificate.IsCertificateAuthority)),
            Field("Certificate.Field.PathLength", "Path length", certificate.PathLength?.ToString(CultureInfo.InvariantCulture) ?? none),
            Field("Certificate.Field.KeyUsage", "Key usage", certificate.KeyUsages.Count == 0 ? none : string.Join(", ", certificate.KeyUsages)),
            Field("Certificate.Field.ExtendedKeyUsage", "Extended key usage", certificate.EnhancedKeyUsages.Count == 0 ? none : string.Join(", ", certificate.EnhancedKeyUsages)),
            Field("Certificate.Field.Sha256", "SHA-256", certificate.Sha256Fingerprint),
            Field("Certificate.Field.Sha1", "SHA-1", certificate.Sha1Fingerprint),
        ];
    }

    /// <summary>The validity part of the status line: valid until, expired on, or not valid before.</summary>
    public static string Validity(CertificateInfo certificate, DateTimeOffset now) => certificate.ValidityAt(now) switch
    {
        CertificateValidity.Expired => string.Format(CultureInfo.CurrentCulture, UiText.Get("Certificate.Status.Expired", "expired on {0}"), Date(certificate.NotAfter)),
        CertificateValidity.NotYetValid => string.Format(CultureInfo.CurrentCulture, UiText.Get("Certificate.Status.NotYetValid", "not valid before {0}"), Date(certificate.NotBefore)),
        _ => string.Format(CultureInfo.CurrentCulture, UiText.Get("Certificate.Status.Valid", "valid until {0}"), Date(certificate.NotAfter)),
    };

    private static string Field(string key, string fallback, string value) =>
        $"{UiText.Get(key, fallback)}: {value}";

    private static string Yes(bool value) => value
        ? UiText.Get("Certificate.Value.Yes", "yes")
        : UiText.Get("Certificate.Value.No", "no");

    private static string Date(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
