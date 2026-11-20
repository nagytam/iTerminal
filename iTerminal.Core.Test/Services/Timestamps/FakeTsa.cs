using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using iTerminal.Core.Services.Timestamps;

namespace iTerminal.Core.Test.Services.Timestamps;

/// <summary>
/// A time-stamp authority that answers from memory. It writes real TSTInfo structures and signs them in CMS with a
/// self-signed certificate that carries the time-stamping purpose, so the token code is tested on genuine DER.
/// </summary>
internal sealed class FakeTsa : ITimestampAuthority
{
    public const string Address = "https://tsa.example/tsr";
    public const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";
    public const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    public static readonly DateTimeOffset GeneratedAt = new(2026, 11, 20, 10, 30, 15, TimeSpan.Zero);

    private readonly X509Certificate2 _certificate = CreateCertificate();

    /// <summary>What imprint the authority writes into its token; the digest itself unless a test wants another.</summary>
    public Func<byte[], byte[]> ImprintFor { get; set; } = digest => digest;

    /// <summary>How many requests the authority was asked for.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<Rfc3161Timestamp?> TimestampAsync(byte[] sha256, CancellationToken cancellationToken = default)
    {
        Calls++;
        var token = Sign(TstInfo(ImprintFor(sha256), 1, GeneratedAt));
        return Task.FromResult<Rfc3161Timestamp?>(new Rfc3161Timestamp(Address, GeneratedAt.UtcDateTime, Convert.ToBase64String(token)));
    }

    /// <summary>A token over <paramref name="imprint"/> carrying <paramref name="nonce"/>, signed by this authority.</summary>
    public byte[] Token(byte[] imprint, BigInteger? nonce) => Sign(TstInfo(imprint, nonce, GeneratedAt));

    /// <summary>A TimeStampResp with <paramref name="status"/> and, when given, <paramref name="token"/>.</summary>
    public static byte[] Response(int status, byte[]? token)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(status);
            }

            if (token is not null)
            {
                writer.WriteEncodedValue(token);
            }
        }

        return writer.Encode();
    }

    private byte[] Sign(byte[] tstInfo)
    {
        var signed = new SignedCms(new ContentInfo(new Oid(TstInfoOid), tstInfo));
        signed.ComputeSignature(new CmsSigner(_certificate));
        return signed.Encode();
    }

    /// <summary>The TSTInfo DER: version 1, a policy, the imprint, a serial, the time, and the nonce when given.</summary>
    public static byte[] TstInfo(byte[] imprint, BigInteger? nonce, DateTimeOffset generated)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1);
            writer.WriteObjectIdentifier("1.2.3.4.1");
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(Sha256Oid);
                    writer.WriteNull();
                }

                writer.WriteOctetString(imprint);
            }

            writer.WriteInteger(42);
            writer.WriteGeneralizedTime(generated);
            if (nonce is { } value)
            {
                writer.WriteInteger(value);
            }
        }

        return writer.Encode();
    }

    private static X509Certificate2 CreateCertificate()
    {
        // Not disposed here: the certificate signs with this key later, so the key must outlive this method.
        var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test time-stamp authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
    }
}
