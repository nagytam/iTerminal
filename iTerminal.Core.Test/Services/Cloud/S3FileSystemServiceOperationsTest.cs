using System.Net;
using iConsole.Data;
using iTerminal.Core.Services.Cloud;

namespace iTerminal.Core.Test.Services.Cloud;

[TestClass]
public class S3FileSystemServiceOperationsTest
{
    private const string Endpoint = "https://s3.example.com";

    private static S3FileSystemService Connected(FakeHttpHandler handler, string endpoint = Endpoint)
    {
        S3FileSystemService service = new(handler);
        service.Connect("AKIAEXAMPLE", "secret", "token", null, endpoint);
        return service;
    }

    private static Url Key(string path) => new("s3://AKIAEXAMPLE@" + path);

    private static HttpResponseMessage Ok(string body = "") => FakeHttpHandler.Reply(HttpStatusCode.OK, body);

    [TestMethod]
    public void NotConnected_ExistenceChecksAreFalse_AndDisconnectForgetsTheAccount()
    {
        FakeHttpHandler handler = new(_ => Ok());
        using S3FileSystemService service = new(handler);

        Assert.IsFalse(service.IsConnected);
        Assert.IsFalse(service.ExistsFile(Key("docs/a.txt")));
        Assert.IsFalse(service.ExistsFolder(Key("docs/")));

        service.Connect("id", "secret", null, " eu-west-1 ", "minio.local:9000");
        Assert.IsTrue(service.IsConnected);
        service.Connect("id", "secret", null, "eu-west-1", "minio.local:9000");
        service.Connect("other", "secret", null, "eu-west-1", "minio.local:9000");
        service.Disconnect();
        Assert.IsFalse(service.IsConnected);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public void Connect_WithoutCredentialsAndWithAnInvalidEndpoint_SendsUnsignedRequests()
    {
        FakeHttpHandler handler = new(_ => Ok());
        using S3FileSystemService service = new(handler);
        service.Connect(null, null, null, null, "http://[bad");

        Assert.IsTrue(service.ExistsFile(Key("public/a.txt")));

        Assert.IsNull(handler.Requests.Single().Header("Authorization"));
    }

    [TestMethod]
    public void ExistsFile_ReflectsTheHeadAnswer_AndTolerantOfBadUrlsAndNetworkErrors()
    {
        FakeHttpHandler handler = new(request => request.Uri.AbsolutePath.EndsWith("missing.txt")
            ? FakeHttpHandler.Reply(HttpStatusCode.NotFound)
            : Ok());
        using S3FileSystemService service = Connected(handler);

        Assert.IsTrue(service.ExistsFile(Key("docs/a.txt")));
        Assert.IsFalse(service.ExistsFile(Key("docs/missing.txt")));
        Assert.IsFalse(service.ExistsFile(Key("docs")), "a bucket is not a file");

        FakeHttpHandler offline = new(_ => throw new HttpRequestException("offline"));
        using S3FileSystemService failing = Connected(offline);
        Assert.IsFalse(failing.ExistsFile(Key("docs/a.txt")));
    }

    [TestMethod]
    public void ExistsFolder_ChecksTheBucketOrAnyKeyUnderThePrefix()
    {
        FakeHttpHandler handler = new(request => request.Uri.Host.StartsWith("empty.")
            || request.Uri.AbsolutePath.StartsWith("/empty")
                ? Ok("<ListBucketResult><KeyCount>0</KeyCount></ListBucketResult>")
                : request.Method == HttpMethod.Head && request.Uri.AbsolutePath.Contains("gone")
                    ? FakeHttpHandler.Reply(HttpStatusCode.NotFound)
                    : Ok("<ListBucketResult><Contents><Key>sub/a</Key></Contents></ListBucketResult>"));
        using S3FileSystemService service = Connected(handler);

        Assert.IsTrue(service.ExistsFolder(Key("")), "the root always exists");
        Assert.IsTrue(service.ExistsFolder(Key("docs")));
        Assert.IsFalse(service.ExistsFolder(Key("gone")));
        Assert.IsTrue(service.ExistsFolder(Key("docs/sub")));
        Assert.IsTrue(service.ExistsFolder(Key("docs/sub/")));
    }

    [TestMethod]
    public void ExistsFolder_WhenTheListingFails_IsFalse()
    {
        FakeHttpHandler handler = new(_ => FakeHttpHandler.Reply(HttpStatusCode.Forbidden));
        using S3FileSystemService service = Connected(handler);

        Assert.IsFalse(service.ExistsFolder(Key("docs/sub")));
    }

    [TestMethod]
    public async Task Download_StreamsTheObject_AndReportsMissingAndFailing()
    {
        FakeHttpHandler handler = new(request => request.Uri.AbsolutePath switch
        {
            "/docs/a.txt" => Ok("contents"),
            "/docs/missing.txt" => FakeHttpHandler.Reply(HttpStatusCode.NotFound),
            _ => FakeHttpHandler.Reply(HttpStatusCode.InternalServerError, "<Error><Code>Boom</Code><Message>bad</Message></Error>"),
        });
        using S3FileSystemService service = Connected(handler);

        using MemoryStream destination = new();
        await service.StreamToAsync(Key("docs/a.txt"), destination, null, CancellationToken.None);
        Assert.AreEqual("contents", System.Text.Encoding.UTF8.GetString(destination.ToArray()));

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => service.StreamToAsync(Key("docs/missing.txt"), new MemoryStream(), null, CancellationToken.None));
        IOException failure = await Assert.ThrowsExactlyAsync<IOException>(
            () => service.StreamToAsync(Key("docs/broken.txt"), new MemoryStream(), null, CancellationToken.None));
        StringAssert.Contains(failure.Message, "Boom");
    }

    [TestMethod]
    public async Task Upload_RefusesABucketAsATarget_AndReportsServerErrors()
    {
        FakeHttpHandler handler = new(_ => FakeHttpHandler.Reply(HttpStatusCode.Forbidden, "<Error><Code>AccessDenied</Code><Message>no</Message></Error>"));
        using S3FileSystemService service = Connected(handler);

        await Assert.ThrowsExactlyAsync<IOException>(
            () => service.StreamFromAsync(Key("docs"), new MemoryStream([1]), null, CancellationToken.None));
        IOException failure = await Assert.ThrowsExactlyAsync<IOException>(
            () => service.StreamFromAsync(Key("docs/a.txt"), new MemoryStream([1]), null, CancellationToken.None));
        StringAssert.Contains(failure.Message, "AccessDenied");
    }

    [TestMethod]
    public async Task CreateFolder_PutsAPlaceholderKey_AndRefusesBuckets()
    {
        FakeHttpHandler handler = new(_ => Ok());
        using S3FileSystemService service = Connected(handler);

        await service.CreateFolderAsync(Key("docs/new"));
        await service.CreateFolderAsync(Key("docs/other/"));

        CollectionAssert.AreEqual(
            new[] { "/docs/new/", "/docs/other/" },
            handler.Requests.Select(r => r.Uri.AbsolutePath).ToArray());
        await Assert.ThrowsExactlyAsync<IOException>(() => service.CreateFolderAsync(Key("docs")));

        FakeHttpHandler denied = new(_ => FakeHttpHandler.Reply(HttpStatusCode.Forbidden));
        using S3FileSystemService failing = Connected(denied);
        await Assert.ThrowsExactlyAsync<IOException>(() => failing.CreateFolderAsync(Key("docs/new")));
    }

    [TestMethod]
    public void DeleteFile_DeletesTheKey_ToleratesMissing_AndRefusesBuckets()
    {
        FakeHttpHandler handler = new(request => request.Uri.AbsolutePath.EndsWith("gone.txt")
            ? FakeHttpHandler.Reply(HttpStatusCode.NotFound)
            : request.Uri.AbsolutePath.EndsWith("locked.txt")
                ? FakeHttpHandler.Reply(HttpStatusCode.Forbidden)
                : FakeHttpHandler.Reply(HttpStatusCode.NoContent));
        using S3FileSystemService service = Connected(handler);

        Assert.IsTrue(service.DeleteFile(Key("docs/a.txt"), moveToRecycleBin: true));
        Assert.IsTrue(service.DeleteFile(Key("docs/gone.txt"), moveToRecycleBin: false));
        Assert.IsFalse(service.DeleteFile(Key("docs"), moveToRecycleBin: false));
        Assert.IsFalse(service.DeleteFolder(Key("docs"), true, false));
        Assert.ThrowsExactly<IOException>(() => service.DeleteFile(Key("docs/locked.txt"), false));
    }

    [TestMethod]
    public async Task Copy_OfAFolder_CopiesEveryKey_AndSkipsExistingTargetsUnlessOverwriting()
    {
        FakeHttpHandler handler = new(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Ok("<ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>src/a.txt</Key></Contents><Contents><Key>src/b.txt</Key></Contents></ListBucketResult>");
            }

            if (request.Method == HttpMethod.Head)
            {
                return request.Uri.AbsolutePath.EndsWith("/a.txt") ? Ok() : FakeHttpHandler.Reply(HttpStatusCode.NotFound);
            }

            return Ok("<CopyObjectResult/>");
        });
        using S3FileSystemService service = Connected(handler);

        await service.CopyAsync(Key("docs/src/"), Key("docs/dst/"), overwrite: false);

        string[] puts = [.. handler.Requests.Where(r => r.Method == HttpMethod.Put).Select(r => r.Uri.AbsolutePath)];
        CollectionAssert.AreEqual(new[] { "/docs/dst/b.txt" }, puts, "a.txt already exists and is kept");

        handler.Requests.Clear();
        await service.CopyAsync(Key("docs/src/"), Key("docs/dst"), overwrite: true);
        Assert.AreEqual(2, handler.Requests.Count(r => r.Method == HttpMethod.Put));
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Head));
    }

    [TestMethod]
    public async Task Move_CopiesThenDeletesTheSource()
    {
        FakeHttpHandler handler = new(request => request.Method == HttpMethod.Put
            ? Ok("<CopyObjectResult/>")
            : request.Method == HttpMethod.Delete ? FakeHttpHandler.Reply(HttpStatusCode.NoContent) : FakeHttpHandler.Reply(HttpStatusCode.NotFound));
        using S3FileSystemService service = Connected(handler);

        await service.MoveAsync(Key("docs/a.txt"), Key("docs/b.txt"), overwrite: false);

        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == HttpMethod.Delete && r.Uri.AbsolutePath == "/docs/a.txt"));

        FakeHttpHandler exists = new(request => request.Method == HttpMethod.Head ? Ok() : FakeHttpHandler.Reply(HttpStatusCode.NoContent));
        using S3FileSystemService keep = Connected(exists);
        await keep.MoveAsync(Key("docs/a.txt"), Key("docs/b.txt"), overwrite: false);
        Assert.AreEqual(0, exists.Requests.Count(r => r.Method != HttpMethod.Head), "an existing target is left alone");
    }

    [TestMethod]
    public async Task MovingAFolder_DeletesEachKeyAfterItsCopy()
    {
        FakeHttpHandler handler = new(request => request.Method == HttpMethod.Get
            ? Ok("<ListBucketResult><Contents><Key>src/a.txt</Key></Contents></ListBucketResult>")
            : request.Method == HttpMethod.Put ? Ok("<CopyObjectResult/>") : FakeHttpHandler.Reply(HttpStatusCode.NoContent));
        using S3FileSystemService service = Connected(handler);

        await service.MoveAsync(new Url("s3://AKIAEXAMPLE@docs/src/"), Key("docs/dst/"), overwrite: true);

        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == HttpMethod.Delete && r.Uri.AbsolutePath == "/docs/src/a.txt"));
    }

    [TestMethod]
    public async Task Copy_RefusesBucketsAndReportsFailuresHiddenInAn200()
    {
        FakeHttpHandler handler = new(request => request.Method == HttpMethod.Head
            ? FakeHttpHandler.Reply(HttpStatusCode.NotFound)
            : Ok("<Error><Code>InternalError</Code><Message>try again</Message></Error>"));
        using S3FileSystemService service = Connected(handler);

        await Assert.ThrowsExactlyAsync<IOException>(() => service.CopyAsync(Key("docs"), Key("other/x"), true));
        IOException hidden = await Assert.ThrowsExactlyAsync<IOException>(
            () => service.CopyAsync(Key("docs/a.txt"), Key("docs/b.txt"), overwrite: false));
        StringAssert.Contains(hidden.Message, "try again");

        FakeHttpHandler noMessage = new(request => request.Method == HttpMethod.Head
            ? FakeHttpHandler.Reply(HttpStatusCode.NotFound)
            : Ok("<Error></Error>"));
        using S3FileSystemService bare = Connected(noMessage);
        IOException unnamed = await Assert.ThrowsExactlyAsync<IOException>(
            () => bare.CopyAsync(Key("docs/a.txt"), Key("docs/b.txt"), overwrite: true));
        StringAssert.Contains(unnamed.Message, "the copy failed");

        FakeHttpHandler rejected = new(_ => FakeHttpHandler.Reply(HttpStatusCode.Forbidden, "<Error><Code>AccessDenied</Code></Error>"));
        using S3FileSystemService denied = Connected(rejected);
        await Assert.ThrowsExactlyAsync<IOException>(() => denied.CopyAsync(Key("docs/a.txt"), Key("docs/b.txt"), overwrite: true));
    }
}
