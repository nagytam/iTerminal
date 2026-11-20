using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

namespace iTerminal.Core.Services.Timestamps;

/// <summary>
/// A time stamp token from a time-stamp authority, kept as the DER bytes the authority signed (base64 in
/// <see cref="Token"/>). It proves that the digest existed no later than <see cref="GeneratedUtc"/>, as
/// the authority's clock said then.
/// </summary>
public sealed record Rfc3161Timestamp(string Authority, DateTime GeneratedUtc, string Token);

/// <summary>The authority could not give a token: no network, a refusal, a malformed or unsigned answer.</summary>
public sealed class TimestampException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// RFC 3161 time stamp requests, responses and tokens, over DER. Only what the evidence notebook needs:
/// SHA-256 imprints, a nonce, and a check of the token's signature against the certificate it carries.
///
/// <para><b>The authority's certificate chain is not checked against trusted roots.</b> The token is proven to
/// be signed by the certificate inside it, and the imprint is proven to be the digest. Whether that
/// certificate belongs to a trusted authority is for the person reading the notebook to judge.</para>
/// </summary>
public static class Rfc3161
{
    /// <summary>The media type of a request, and of an answer.</summary>
    public const string RequestMediaType = "application/timestamp-query";

    /// <summary>The media type of an answer.</summary>
    public const string ResponseMediaType = "application/timestamp-reply";

    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";

    /// <summary>A TimeStampReq for <paramref name="sha256"/>: version 1, the SHA-256 imprint, the nonce, and certReq so the token carries the certificate.</summary>
    public static byte[] BuildRequest(ReadOnlySpan<byte> sha256, BigInteger nonce)
    {
        if (sha256.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A time stamp covers a SHA-256 digest.", nameof(sha256));
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(Sha256Oid);
                    writer.WriteNull();
                }

                writer.WriteOctetString(sha256);
            }

            writer.WriteInteger(nonce);
            writer.WriteBoolean(true);
        }

        return writer.Encode();
    }

    /// <summary>
    /// Reads a TimeStampResp. A granted answer (status 0 or 1) must carry a token whose imprint is
    /// <paramref name="sha256"/>, whose nonce is <paramref name="nonce"/> and whose signature checks out.
    /// </summary>
    /// <exception cref="TimestampException">The answer is a refusal, or is not a valid token for this request.</exception>
    public static Rfc3161Timestamp ParseResponse(ReadOnlySpan<byte> response, ReadOnlySpan<byte> sha256, BigInteger nonce, string authority)
    {
        try
        {
            var reader = new AsnReader(response.ToArray(), AsnEncodingRules.DER);
            var outer = reader.ReadSequence();
            reader.ThrowIfNotEmpty();

            var status = outer.ReadSequence().ReadInteger();
            if (status != 0 && status != 1)
            {
                throw new TimestampException($"The time-stamp authority refused the request (status {status}).");
            }

            if (!outer.HasData)
            {
                throw new TimestampException("The time-stamp authority answered without a token.");
            }

            var tokenDer = outer.ReadEncodedValue().Span.ToArray();
            var info = ReadToken(tokenDer);
            if (!info.Imprint.AsSpan().SequenceEqual(sha256))
            {
                throw new TimestampException("The token is for a different digest.");
            }

            if (info.Nonce != nonce)
            {
                throw new TimestampException("The token answers a different request (nonce).");
            }

            return new Rfc3161Timestamp(authority, info.GeneratedUtc, Convert.ToBase64String(tokenDer));
        }
        catch (Exception exception) when (exception is AsnContentException or CryptographicException or OverflowException)
        {
            throw new TimestampException("The time-stamp authority's answer is not a valid token.", exception);
        }
    }

    /// <summary>
    /// True when <paramref name="timestamp"/> is a token over <paramref name="sha256"/> whose signature checks out.
    /// Never reaches the network: it works on the stored bytes.
    /// </summary>
    public static bool IsValidFor(Rfc3161Timestamp timestamp, ReadOnlySpan<byte> sha256)
    {
        try
        {
            var info = ReadToken(Convert.FromBase64String(timestamp.Token));
            return info.Imprint.AsSpan().SequenceEqual(sha256);
        }
        catch (Exception exception) when (exception is FormatException or AsnContentException or CryptographicException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>The fields of a TSTInfo the notebook reads, after its signature has been checked.</summary>
    private sealed record TokenInfo(byte[] Imprint, BigInteger? Nonce, DateTime GeneratedUtc);

    /// <summary>Decodes the token, checks the signature against the certificate inside it, and reads its TSTInfo.</summary>
    private static TokenInfo ReadToken(byte[] tokenDer)
    {
        var signed = new SignedCms();
        signed.Decode(tokenDer);
        if (signed.ContentInfo.ContentType.Value != TstInfoOid)
        {
            throw new TimestampException("The token does not hold a time-stamp info.");
        }

        signed.CheckSignature(verifySignatureOnly: true);

        var tst = new AsnReader(signed.ContentInfo.Content, AsnEncodingRules.DER).ReadSequence();
        tst.ReadInteger(); // version
        tst.ReadObjectIdentifier(); // policy

        var imprintReader = tst.ReadSequence();
        imprintReader.ReadSequence(); // the hash algorithm
        var imprint = imprintReader.ReadOctetString();
        if (imprint.Length != SHA256.HashSizeInBytes)
        {
            throw new TimestampException("The token's imprint is not a SHA-256 digest.");
        }

        tst.ReadInteger(); // serial number
        var generated = tst.ReadGeneralizedTime().UtcDateTime;

        // accuracy, ordering, nonce, tsa and extensions are optional and follow in that order; only the nonce matters here.
        BigInteger? nonce = null;
        while (tst.HasData)
        {
            var tag = tst.PeekTag();
            if (tag.TagClass == TagClass.Universal && tag.TagValue == (int)UniversalTagNumber.Integer)
            {
                nonce = tst.ReadInteger();
            }
            else
            {
                tst.ReadEncodedValue();
            }
        }

        return new TokenInfo(imprint, nonce, generated);
    }
}
