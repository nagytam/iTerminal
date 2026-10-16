using System.Net;
using System.Security.Cryptography;
using iConsole.Data;
using iTerminal.Core.Services.Cloud;

namespace iTerminal.Core.Test.Services.Cloud;

[TestClass]
public class S3FileSystemServiceTest
{
    private const string BucketListing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
          <Name>photos</Name><Prefix>2026/</Prefix><KeyCount>3</KeyCount><IsTruncated>false</IsTruncated>
          <Contents><Key>2026/</Key><Size>0</Size><LastModified>2026-01-01T00:00:00.000Z</LastModified></Contents>
          <Contents><Key>2026/holiday #1.jpg</Key><Size>2048</Size><LastModified>2026-02-03T10:30:00.000Z</LastModified></Contents>
          <CommonPrefixes><Prefix>2026/summer/</Prefix></CommonPrefixes>
        </ListBucketResult>
        """;

    private static S3FileSystemService Connected(FakeHttpHandler handler, string? endpoint = null, string? region = null)
    {
        var service = new S3FileSystemService(handler);
        service.Connect("AKIAEXAMPLE", "secret", null, region, endpoint);
        return service;
    }

    [TestMethod]
    public async Task List_ReadsFoldersAndFiles_FromAPrefixListing_WithoutThePlaceholderKey()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, BucketListing));
        using var service = Connected(handler, endpoint: "https://minio.local:9000");

        var entries = await service.ListAsync(new Url("s3://AKIAEXAMPLE@photos/2026/"), CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "s3://AKIAEXAMPLE@photos/2026/summer/", "s3://AKIAEXAMPLE@photos/2026/holiday #1.jpg" },
            entries.Select(e => e.Url.FullUrl).ToArray());
        Assert.AreEqual(PathType.Folder, entries[0].Url.PathType);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("minio.local", request.Uri.Host);
        Assert.AreEqual("/photos/", request.Uri.AbsolutePath, "a custom endpoint is addressed path-style");
        StringAssert.Contains(request.Query, "prefix=2026/");
        StringAssert.Contains(request.Query, "delimiter=/");
        StringAssert.StartsWith(request.Header("Authorization"), "AWS4-HMAC-SHA256 Credential=AKIAEXAMPLE/");
        Assert.IsNotNull(request.Header("x-amz-date"));
    }

    [TestMethod]
    public async Task List_WithoutABucket_ListsTheBuckets()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, """
            <ListAllMyBucketsResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><Buckets>
              <Bucket><Name>beta</Name><CreationDate>2025-05-05T00:00:00.000Z</CreationDate></Bucket>
              <Bucket><Name>alpha</Name><CreationDate>2025-04-04T00:00:00.000Z</CreationDate></Bucket>
            </Buckets></ListAllMyBucketsResult>
            """));
        using var service = Connected(handler);

        var entries = await service.ListAsync(new Url("s3://AKIAEXAMPLE@/"), CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "s3://AKIAEXAMPLE@alpha/", "s3://AKIAEXAMPLE@beta/" },
            entries.Select(e => e.Url.FullUrl).ToArray());
        Assert.AreEqual("s3.amazonaws.com", handler.Requests.Single().Uri.Host);
    }

    [TestMethod]
    public async Task ABucketInAnotherRegion_IsLearnedFromTheAnswer_AndTheRequestRepeated()
    {
        var first = true;
        var handler = new FakeHttpHandler(request =>
        {
            if (first)
            {
                first = false;
                var redirect = FakeHttpHandler.Reply(HttpStatusCode.MovedPermanently);
                redirect.Headers.Add("x-amz-bucket-region", "eu-west-1");
                return redirect;
            }

            return FakeHttpHandler.Reply(HttpStatusCode.OK, BucketListing);
        });
        using var service = Connected(handler);

        await service.ListAsync(new Url("s3://AKIAEXAMPLE@photos/"), CancellationToken.None);

        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual("photos.s3.amazonaws.com", handler.Requests[0].Uri.Host);
        Assert.AreEqual("photos.s3.eu-west-1.amazonaws.com", handler.Requests[1].Uri.Host);
        StringAssert.Contains(handler.Requests[1].Header("Authorization"), "/eu-west-1/s3/aws4_request");
    }

    [TestMethod]
    public async Task Upload_OverPlainHttp_DeclaresTheHashOfTheBody()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK));
        using var service = Connected(handler, endpoint: "http://localhost:9000");
        var body = "hello s3"u8.ToArray();

        await service.StreamFromAsync(new Url("s3://AKIAEXAMPLE@docs/a b.txt"), new MemoryStream(body), null, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Put, request.Method);
        Assert.AreEqual("/docs/a%20b.txt", request.Uri.AbsolutePath);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(body)), request.Header("x-amz-content-sha256"));
        CollectionAssert.AreEqual(body, request.Body);
    }

    [TestMethod]
    public async Task Upload_OverTls_DoesNotHashTheBody()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK));
        using var service = Connected(handler, endpoint: "https://s3.example.com");

        await service.StreamFromAsync(new Url("s3://AKIAEXAMPLE@docs/a.txt"), new MemoryStream([1, 2, 3]), null, CancellationToken.None);

        Assert.AreEqual("UNSIGNED-PAYLOAD", handler.Requests.Single().Header("x-amz-content-sha256"));
    }

    [TestMethod]
    public void DeleteFolder_RemovesEveryKeyUnderThePrefix()
    {
        var handler = new FakeHttpHandler(request => request.Method == HttpMethod.Get
            ? FakeHttpHandler.Reply(HttpStatusCode.OK, """
                <ListBucketResult><IsTruncated>false</IsTruncated>
                  <Contents><Key>old/a.txt</Key></Contents><Contents><Key>old/sub/b.txt</Key></Contents>
                </ListBucketResult>
                """)
            : FakeHttpHandler.Reply(HttpStatusCode.NoContent));
        using var service = Connected(handler, endpoint: "https://s3.example.com");

        var deleted = service.DeleteFolder(new Url("s3://AKIAEXAMPLE@docs/old/"), recursive: true, moveToRecycleBin: false);

        Assert.IsTrue(deleted);
        var deletes = handler.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Uri.AbsolutePath).ToArray();
        CollectionAssert.AreEquivalent(new[] { "/docs/old/a.txt", "/docs/old/sub/b.txt" }, deletes);
    }

    [TestMethod]
    public async Task Copy_UsesAServerSideCopy_NamingTheSource()
    {
        var handler = new FakeHttpHandler(request => request.Method == HttpMethod.Head
            ? FakeHttpHandler.Reply(HttpStatusCode.NotFound)
            : FakeHttpHandler.Reply(HttpStatusCode.OK, "<CopyObjectResult><ETag>\"x\"</ETag></CopyObjectResult>"));
        using var service = Connected(handler, endpoint: "https://s3.example.com");

        await service.CopyAsync(new Url("s3://AKIAEXAMPLE@docs/a b.txt"), new Url("s3://AKIAEXAMPLE@archive/2026/a b.txt"), overwrite: false);

        var put = handler.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.AreEqual("/archive/2026/a%20b.txt", put.Uri.AbsolutePath);
        Assert.AreEqual("/docs/a%20b.txt", put.Header("x-amz-copy-source"));
    }

    [TestMethod]
    public async Task AnError_IsReportedWithTheCodeAndMessageS3Gave()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.Forbidden,
            "<Error><Code>AccessDenied</Code><Message>Access Denied</Message></Error>"));
        using var service = Connected(handler, endpoint: "https://s3.example.com");

        var exception = await Assert.ThrowsExactlyAsync<IOException>(
            () => service.ListAsync(new Url("s3://AKIAEXAMPLE@locked/"), CancellationToken.None));

        StringAssert.Contains(exception.Message, "AccessDenied");
    }

    [TestMethod]
    public void Escape_KeepsSlashesAndEncodesTheRest()
    {
        Assert.AreEqual("a%20b/c%23d/e.txt", S3FileSystemService.EscapeKey("a b/c#d/e.txt"));
    }
}
