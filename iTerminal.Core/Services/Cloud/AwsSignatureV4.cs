using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace iTerminal.Core.Services.Cloud;

/// <summary>
/// AWS Signature Version 4 for the S3 API, written out rather than pulled in as the AWS SDK: the
/// drive needs six requests, and the SDK is tens of megabytes of every other service. The algorithm
/// is public and checked against AWS's own worked example in the tests.
/// </summary>
public static class AwsSignatureV4
{
    /// <summary>The SHA-256 of nothing, the payload hash of a request with no body.</summary>
    public const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>The payload hash a request sent over TLS may declare instead of hashing a large body.</summary>
    public const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static async Task<string> Sha256HexAsync(Stream stream, CancellationToken cancellationToken)
    {
        var start = stream.Position;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = start;
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Builds the <c>Authorization</c> header value.
    /// </summary>
    /// <param name="method">HTTP method, upper case.</param>
    /// <param name="canonicalUri">The absolute, already percent-encoded path.</param>
    /// <param name="query">Query parameters (unencoded; they are encoded and sorted here).</param>
    /// <param name="headers">Every header to sign, including <c>host</c> and <c>x-amz-date</c>; names are lowered here.</param>
    /// <param name="payloadHash">Hex SHA-256 of the body, or <see cref="UnsignedPayload"/>.</param>
    public static string Authorize(
        string method,
        string canonicalUri,
        IEnumerable<KeyValuePair<string, string>> query,
        IReadOnlyDictionary<string, string> headers,
        string payloadHash,
        string accessKeyId,
        string secretAccessKey,
        string region,
        string service,
        DateTime timestampUtc,
        out string signedHeaders,
        out string signature)
    {
        var amzDate = timestampUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = timestampUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var canonicalQuery = string.Join('&', query
            .Select(p => (Key: Uri.EscapeDataString(p.Key), Value: Uri.EscapeDataString(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value));

        var sorted = headers
            .Select(h => (Name: h.Key.ToLowerInvariant(), Value: string.Join(' ', h.Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))))
            .OrderBy(h => h.Name, StringComparer.Ordinal)
            .ToList();
        var canonicalHeaders = string.Concat(sorted.Select(h => h.Name + ":" + h.Value + "\n"));
        signedHeaders = string.Join(';', sorted.Select(h => h.Name));

        var canonicalRequest = string.Join('\n', method, canonicalUri, canonicalQuery, canonicalHeaders, signedHeaders, payloadHash);

        var scope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest)));

        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), date);
        key = Hmac(key, region);
        key = Hmac(key, service);
        key = Hmac(key, "aws4_request");
        signature = Convert.ToHexStringLower(Hmac(key, stringToSign));

        return $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}
