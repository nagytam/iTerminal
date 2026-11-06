using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iConsole.Executables;

namespace iTerminal.Core.Services.Executables;

/// <summary>
/// Lists the certificates inside an Authenticode signature (a PKCS #7 SignedData blob) without checking anything: the blob is walked with
/// the BCL's ASN.1 reader, so no package and no process is involved. Whether the signature is valid is a question for the signing dialog.
/// </summary>
internal static class AuthenticodeCertificates
{
    public const int MaxCertificates = 16;

    /// <summary>Reads the certificates; an empty list for a blob that is not SignedData or is damaged. Never throws for bad input.</summary>
    public static IReadOnlyList<ExecutableCertificate> Parse(ReadOnlyMemory<byte> blob)
    {
        List<ExecutableCertificate> result = [];
        try
        {
            AsnReader contentInfo = new AsnReader(blob, AsnEncodingRules.BER).ReadSequence();
            contentInfo.ReadObjectIdentifier();
            AsnReader signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
            signedData.ReadInteger();
            signedData.ReadSetOf();
            signedData.ReadSequence();
            if (!signedData.HasData || !signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                return result;
            }

            AsnReader certificates = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
            while (certificates.HasData && result.Count < MaxCertificates)
            {
                byte[] der = certificates.ReadEncodedValue().ToArray();
                try
                {
                    using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(der);
                    result.Add(new ExecutableCertificate(
                        BoundedFileReader.Printable(certificate.Subject),
                        BoundedFileReader.Printable(certificate.Issuer),
                        certificate.NotBefore,
                        certificate.NotAfter,
                        certificate.Thumbprint));
                }
                catch (CryptographicException)
                {
                    // One certificate that cannot be read does not hide the others.
                }
            }
        }
        catch (Exception ex) when (ex is AsnContentException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            // Not SignedData, or cut short: report what was read.
        }

        return result;
    }
}
