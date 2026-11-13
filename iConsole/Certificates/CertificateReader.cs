using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace iConsole.Certificates;

/// <summary>
/// Reads the certificates in a PEM bundle or a single DER certificate. PEM is split here, block by block: the BCL reads one
/// certificate at a time. A private-key block is counted and never decoded. Bad input gives an empty list and an error
/// text; nothing is thrown.
/// </summary>
public static partial class CertificateReader
{
    private const string BeginCertificate = "-----BEGIN CERTIFICATE-----";
    private const string EndCertificate = "-----END CERTIFICATE-----";

    [GeneratedRegex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBegin();

    /// <summary>
    /// Reads the certificates in <paramref name="bytes"/>. Any failure, whatever its type, comes back as an empty list and an
    /// error text, so the caller always has a result to act on.
    /// </summary>
    public static CertificateReadResult Read(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            string text = Encoding.UTF8.GetString(bytes.Span);
            if (text.Contains("-----BEGIN ", StringComparison.Ordinal))
            {
                return ReadPem(text);
            }

            return ReadDer(bytes.ToArray());
        }
        catch (Exception exception)
        {
            return new CertificateReadResult([], 0, exception.Message);
        }
    }

    private static CertificateReadResult ReadPem(string text)
    {
        List<CertificateInfo> certificates = [];
        string? error = null;
        int position = 0;
        while (certificates.Count < CertificateLimits.MaxCertificates)
        {
            int begin = text.IndexOf(BeginCertificate, position, StringComparison.Ordinal);
            if (begin < 0)
            {
                break;
            }

            int bodyStart = begin + BeginCertificate.Length;
            int end = text.IndexOf(EndCertificate, bodyStart, StringComparison.Ordinal);
            if (end < 0)
            {
                error ??= "A certificate block has no end line.";
                break;
            }

            position = end + EndCertificate.Length;
            // One pass over the block: the base64 body without its line breaks, with no intermediate copies.
            StringBuilder body = new(end - bodyStart);
            foreach (char c in text.AsSpan(bodyStart, end - bodyStart))
            {
                if (!char.IsWhiteSpace(c))
                {
                    body.Append(c);
                }
            }
            try
            {
                byte[] der = Convert.FromBase64String(body.ToString());
                certificates.Add(Describe(X509CertificateLoader.LoadCertificate(der)));
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
            {
                error ??= "A certificate block could not be read.";
            }
        }

        return new CertificateReadResult(certificates, PrivateKeyBegin().Matches(text).Count, error);
    }

    private static CertificateReadResult ReadDer(byte[] der)
    {
        try
        {
            return new CertificateReadResult([Describe(X509CertificateLoader.LoadCertificate(der))], 0, null);
        }
        catch (CryptographicException exception)
        {
            return new CertificateReadResult([], 0, exception.Message);
        }
    }

    private static CertificateInfo Describe(X509Certificate2 certificate)
    {
        using (certificate)
        {
            string subject = Printable(certificate.Subject);
            string issuer = Printable(certificate.Issuer);
            return new CertificateInfo(
                Subject: subject,
                Issuer: issuer,
                SerialNumber: certificate.SerialNumber,
                NotBefore: new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),
                NotAfter: new DateTimeOffset(certificate.NotAfter.ToUniversalTime()),
                SignatureAlgorithm: Printable(certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? string.Empty),
                PublicKeyAlgorithm: Printable(certificate.PublicKey.Oid.FriendlyName ?? certificate.PublicKey.Oid.Value ?? string.Empty),
                KeySize: KeySizeOf(certificate),
                SubjectAlternativeNames: SubjectAlternativeNames(certificate),
                IsCertificateAuthority: BasicConstraints(certificate, out int? pathLength),
                PathLength: pathLength,
                KeyUsages: KeyUsages(certificate),
                EnhancedKeyUsages: EnhancedKeyUsages(certificate),
                Sha256Fingerprint: Convert.ToHexString(SHA256.HashData(certificate.RawData)),
                Sha1Fingerprint: Convert.ToHexString(SHA1.HashData(certificate.RawData)),
                SelfIssued: string.Equals(subject, issuer, StringComparison.Ordinal));
        }
    }

    private static int KeySizeOf(X509Certificate2 certificate)
    {
        try
        {
            // Each key object is disposed: the BCL hands out a fresh native handle for every call.
            switch (certificate.PublicKey.Oid.Value)
            {
                case "1.2.840.113549.1.1.1":
                    using (RSA? rsa = certificate.PublicKey.GetRSAPublicKey())
                    {
                        return rsa?.KeySize ?? 0;
                    }
                case "1.2.840.10045.2.1":
                    using (ECDsa? ecdsa = certificate.PublicKey.GetECDsaPublicKey())
                    {
                        return ecdsa?.KeySize ?? 0;
                    }
                default:
                    return 0;
            }
        }
        catch (CryptographicException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<string> SubjectAlternativeNames(X509Certificate2 certificate)
    {
        List<string> names = [];
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            try
            {
                AsnReader sequence = new(extension.RawData, AsnEncodingRules.DER);
                AsnReader names1 = sequence.ReadSequence();
                while (names1.HasData)
                {
                    Asn1Tag tag = names1.PeekTag();
                    if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 2)
                    {
                        names.Add(Printable(names1.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 2))));
                    }
                    else if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 1)
                    {
                        names.Add("email: " + Printable(names1.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 1))));
                    }
                    else if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 7)
                    {
                        byte[] address = names1.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7));
                        names.Add(address.Length is 4 or 16 ? new System.Net.IPAddress(address).ToString() : "IP " + Convert.ToHexString(address));
                    }
                    else
                    {
                        names1.ReadEncodedValue();
                    }

                    if (names.Count >= CertificateLimits.MaxNames)
                    {
                        break;
                    }
                }
            }
            catch (Exception exception) when (exception is AsnContentException or FormatException or ArgumentException)
            {
                // A damaged SAN extension shows the names read before the damage.
            }
        }

        return names;
    }

    private static bool BasicConstraints(X509Certificate2 certificate, out int? pathLength)
    {
        pathLength = null;
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension constraints)
            {
                pathLength = constraints.HasPathLengthConstraint ? constraints.PathLengthConstraint : null;
                return constraints.CertificateAuthority;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> KeyUsages(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509KeyUsageExtension usage)
            {
                return Enum.GetValues<X509KeyUsageFlags>()
                    .Where(flag => flag != X509KeyUsageFlags.None && usage.KeyUsages.HasFlag(flag))
                    .Select(flag => flag.ToString())
                    .ToList();
            }
        }

        return [];
    }

    private static IReadOnlyList<string> EnhancedKeyUsages(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509EnhancedKeyUsageExtension usage)
            {
                return usage.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                    .Select(oid => Printable(oid.FriendlyName ?? oid.Value ?? string.Empty))
                    .ToList();
            }
        }

        return [];
    }

    /// <summary>
    /// Replaces control and format characters (bidi overrides, zero-width joiners and the like), so text from a certificate
    /// cannot move the cursor or reorder what the screen shows.
    /// </summary>
    public static string Printable(string text)
    {
        char[] chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(chars[i]);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                chars[i] = '?';
            }
        }

        return new string(chars);
    }
}
