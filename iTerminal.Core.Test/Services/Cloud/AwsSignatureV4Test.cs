using iTerminal.Core.Services.Cloud;

namespace iTerminal.Core.Test.Services.Cloud;

/// <summary>Checked against the worked examples in Amazon's Signature Version 4 documentation for S3.</summary>
[TestClass]
public class AwsSignatureV4Test
{
    private const string AccessKey = "AKIAIOSFODNN7EXAMPLE";
    private const string Secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private static readonly DateTime Timestamp = new(2013, 5, 24, 0, 0, 0, DateTimeKind.Utc);

    [Timeout(60_000)]
    [TestMethod]
    public void GetObject_WithARangeHeader_MatchesTheDocumentedSignature()
    {
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = AwsSignatureV4.EmptyPayloadHash,
            ["x-amz-date"] = "20130524T000000Z",
        };

        var authorization = AwsSignatureV4.Authorize(
            "GET", "/test.txt", [], headers, AwsSignatureV4.EmptyPayloadHash,
            AccessKey, Secret, "us-east-1", "s3", Timestamp, out var signedHeaders, out var signature);

        Assert.AreEqual("host;range;x-amz-content-sha256;x-amz-date", signedHeaders);
        Assert.AreEqual("f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", signature);
        Assert.AreEqual(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request,"
            + "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date,"
            + "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            authorization.Replace(", ", ","));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PutObject_WithABodyAndAnEscapedKey_MatchesTheDocumentedSignature()
    {
        var body = "Welcome to Amazon S3."u8.ToArray();
        var payloadHash = AwsSignatureV4.Sha256Hex(body);
        Assert.AreEqual("44ce7dd67c959e0d3524ffac1771dfbba87d2b6b4b4e99e42034a8b803f8b072", payloadHash);

        var headers = new Dictionary<string, string>
        {
            ["date"] = "Fri, 24 May 2013 00:00:00 GMT",
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = "20130524T000000Z",
            ["x-amz-storage-class"] = "REDUCED_REDUNDANCY",
        };

        AwsSignatureV4.Authorize(
            "PUT", "/test%24file.text", [], headers, payloadHash,
            AccessKey, Secret, "us-east-1", "s3", Timestamp, out _, out var signature);

        Assert.AreEqual("98ad721746da40c64f1a55b78f14c238d841ea1380cd77a1b5971af0ece108bd", signature);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListObjects_SortsAndEncodesTheQuery_AndMatchesTheDocumentedSignature()
    {
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["x-amz-content-sha256"] = AwsSignatureV4.EmptyPayloadHash,
            ["x-amz-date"] = "20130524T000000Z",
        };

        // Given out of order on purpose: the signature depends on the sorted, encoded form.
        List<KeyValuePair<string, string>> query = [new("prefix", "J"), new("max-keys", "2")];
        AwsSignatureV4.Authorize(
            "GET", "/", query, headers, AwsSignatureV4.EmptyPayloadHash,
            AccessKey, Secret, "us-east-1", "s3", Timestamp, out _, out var signature);

        Assert.AreEqual("34b48302e7b5fa45bde8084f4b7868a86f0a534bc59db6670ed5711ef69dc6f7", signature);
    }
}
