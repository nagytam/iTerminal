using System.Net;
using System.Security.Cryptography;
using System.Text;
using iConsole.Data;
using iTerminal.Core.Test.Helper;
using iTerminal.Core.Test.Services.Cloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Cloud.Test;

/// <summary>What the Azure Blob drive asks of the REST API, how it signs, and what it makes of the answers.</summary>
[TestClass]
public class AzureBlobFileSystemServiceTest
{
    private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");
    private static readonly string Key = Convert.ToBase64String(KeyBytes);

    private static AzureBlobFileSystemService Service(FakeHttpHandler fake, string? key = null) =>
        new(account => new AzureBlobCredentials(account, key ?? Key, null), fake);

    private static string Container(string name, string modified) =>
        $"<Container><Name>{name}</Name><Properties><Last-Modified>{modified}</Last-Modified></Properties></Container>";

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheAccountRoot_ListsItsContainers()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK,
            $"<EnumerationResults><Containers>{Container("photos", "Tue, 02 Jan 2024 10:00:00 GMT")}</Containers><NextMarker /></EnumerationResults>"));
        var service = Service(fake);

        var rows = await service.ListAsync(new Url("azblob://acc/"), CancellationToken.None);

        Assert.AreEqual("photos", rows.Single().Url.Name);
        Assert.AreEqual("azblob://acc/photos/", rows.Single().Url.FullUrl);
        Assert.AreEqual(PathType.Folder, rows.Single().Url.PathType);
        Assert.AreEqual("https://acc.blob.core.windows.net/?comp=list", fake.Requests.Single().Uri.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AContainer_ListsFoldersAndFiles_SkipsTheFolderMarker_AndFollowsTheMarker()
    {
        var fake = new FakeHttpHandler(seen =>
        {
            if (seen.Query.Contains("marker=m1", StringComparison.Ordinal))
            {
                return FakeHttpHandler.Reply(HttpStatusCode.OK,
                    "<EnumerationResults><Blobs><Blob><Name>2024/b.jpg</Name><Properties><Content-Length>9</Content-Length></Properties></Blob></Blobs><NextMarker /></EnumerationResults>");
            }

            return FakeHttpHandler.Reply(HttpStatusCode.OK,
                """
                <EnumerationResults><Blobs>
                  <BlobPrefix><Name>2024/Trips/</Name></BlobPrefix>
                  <Blob><Name>2024/</Name><Properties><Content-Length>0</Content-Length></Properties></Blob>
                  <Blob><Name>2024/a.jpg</Name><Properties><Content-Length>123</Content-Length><Last-Modified>Tue, 02 Jan 2024 10:00:00 GMT</Last-Modified></Properties></Blob>
                </Blobs><NextMarker>m1</NextMarker></EnumerationResults>
                """);
        });
        var service = Service(fake);

        var rows = await service.ListAsync(new Url("azblob://acc/photos/2024/"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Trips", "a.jpg", "b.jpg" }, rows.Select(r => r.Url.Name).ToArray());
        Assert.AreEqual(PathType.Folder, rows[0].Url.PathType);
        Assert.AreEqual(PathType.File, rows[1].Url.PathType);
        StringAssert.Contains(fake.Requests[0].Query, "prefix=2024/");
        StringAssert.Contains(fake.Requests[0].Query, "delimiter=/");
        StringAssert.Contains(fake.Requests[0].Query, "restype=container");
        StringAssert.Contains(fake.Requests[1].Query, "marker=m1");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASmallFile_IsPutAsABlockBlob_SignedWithTheAccountKey()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.Created, string.Empty));
        var service = Service(fake);

        await service.WriteFileAsync(new Url("azblob://acc/photos/new.txt"), new MemoryStream(Encoding.UTF8.GetBytes("data")));

        var request = fake.Requests.Single();
        Assert.AreEqual(HttpMethod.Put, request.Method);
        Assert.AreEqual("https://acc.blob.core.windows.net/photos/new.txt", request.Uri.ToString());
        Assert.AreEqual("BlockBlob", request.Header("x-ms-blob-type"));
        Assert.AreEqual("2023-11-03", request.Header("x-ms-version"));
        Assert.AreEqual("data", request.Text);
        Assert.IsTrue(request.Header("Authorization")!.StartsWith("SharedKey acc:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheStringToSign_HasTheTwelveStandardFields_TheMsHeaders_AndTheCanonicalResource()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://acc.blob.core.windows.net/photos?restype=container&comp=list");
        request.Headers.TryAddWithoutValidation("x-ms-date", "Mon, 01 Jan 2024 00:00:00 GMT");
        request.Headers.TryAddWithoutValidation("x-ms-version", "2023-11-03");

        var expected = "GET" + new string('\n', 12)
            + "x-ms-date:Mon, 01 Jan 2024 00:00:00 GMT\n"
            + "x-ms-version:2023-11-03\n"
            + "/acc/photos\ncomp:list\nrestype:container";
        Assert.AreEqual(expected, AzureBlobFileSystemService.StringToSign(request, "acc"));
    }

    [TestMethod]
    public void TheSignature_IsTheHmacOfTheStringToSign()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://acc.blob.core.windows.net/photos/a.txt");
        request.Headers.TryAddWithoutValidation("x-ms-date", "Mon, 01 Jan 2024 00:00:00 GMT");

        using var hmac = new HMACSHA256(KeyBytes);
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(AzureBlobFileSystemService.StringToSign(request, "acc"))));

        Assert.AreEqual(expected, AzureBlobFileSystemService.Sign(request, "acc", Key));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoKey_RequestsAreAnonymous()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, "<EnumerationResults><Blobs /><NextMarker /></EnumerationResults>"));
        var service = new AzureBlobFileSystemService(account => new AzureBlobCredentials(account, null, null), fake);

        await service.ListAsync(new Url("azblob://public/open/"), CancellationToken.None);

        Assert.IsNull(fake.Requests.Single().Header("Authorization"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMissingBlob_DownloadsAsNothing()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.NotFound, "<Error><Code>BlobNotFound</Code></Error>"));
        var service = Service(fake);

        var file = await service.ReadFileAsync(new Url("azblob://acc/photos/none.txt"));

        Assert.IsNull(file);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACopyInsideOneAccount_IsAServerSideCopyBlob()
    {
        var fake = new FakeHttpHandler(seen =>
        {
            if (seen.Method == HttpMethod.Head)
            {
                return FakeHttpHandler.Reply(HttpStatusCode.NotFound, string.Empty);
            }

            var copied = FakeHttpHandler.Reply(HttpStatusCode.Accepted, string.Empty);
            copied.Headers.Add("x-ms-copy-status", "success");
            return copied;
        });
        var service = Service(fake);

        await service.CopyAsync(new Url("azblob://acc/photos/a.jpg"), new Url("azblob://acc/photos/b.jpg"), overwrite: false);

        var copy = fake.Requests.Last();
        Assert.AreEqual(HttpMethod.Put, copy.Method);
        Assert.AreEqual("https://acc.blob.core.windows.net/photos/a.jpg", copy.Header("x-ms-copy-source"));
        Assert.AreEqual("https://acc.blob.core.windows.net/photos/b.jpg", copy.Uri.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolder_IsAZeroByteMarkerWithATrailingSlash_AndNeverOverwritten()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.PreconditionFailed, "<Error><Code>BlobAlreadyExists</Code></Error>"));
        var service = Service(fake);

        await service.CreateFolderAsync(new Url("azblob://acc/photos/2025/Trips"));

        var request = fake.Requests.Single();
        Assert.AreEqual("https://acc.blob.core.windows.net/photos/2025/Trips/", request.Uri.ToString());
        Assert.AreEqual("*", request.Header("If-None-Match"));
        Assert.AreEqual("BlockBlob", request.Header("x-ms-blob-type"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderWithAMarker_CopiesItsContentsUnderTheTargetName_AndNeverAsksForTheMarkerWithoutTheSlash()
    {
        var fake = new FakeHttpHandler(seen =>
        {
            if (seen.Method == HttpMethod.Get)
            {
                return FakeHttpHandler.Reply(HttpStatusCode.OK,
                    "<EnumerationResults><Blobs><Blob><Name>src/</Name><Properties><Content-Length>0</Content-Length></Properties></Blob>"
                    + "<Blob><Name>src/a.txt</Name><Properties><Content-Length>3</Content-Length></Properties></Blob></Blobs><NextMarker /></EnumerationResults>");
            }

            if (seen.Method == HttpMethod.Head)
            {
                return FakeHttpHandler.Reply(HttpStatusCode.NotFound, string.Empty);
            }

            var reply = FakeHttpHandler.Reply(HttpStatusCode.Accepted, string.Empty);
            reply.Headers.Add("x-ms-copy-status", "success");
            return reply;
        });
        var service = Service(fake);

        await service.CopyAsync(new Url("azblob://acc/c/src/"), new Url("azblob://acc/c/dst/"), overwrite: true);

        var copies = fake.Requests.Where(r => r.Method == HttpMethod.Put && r.Header("x-ms-copy-source") is not null).ToList();
        Assert.AreEqual(1, copies.Count, "Only the file is server-copied.");
        Assert.AreEqual("https://acc.blob.core.windows.net/c/src/a.txt", copies[0].Header("x-ms-copy-source"));
        Assert.AreEqual("https://acc.blob.core.windows.net/c/dst/a.txt", copies[0].Uri.ToString());
        Assert.IsTrue(fake.Requests.Any(r => r.Uri.ToString() == "https://acc.blob.core.windows.net/c/dst/" && r.Header("If-None-Match") == "*"),
            "The empty target folder gets its own marker.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANonEmptyContainer_IsNotDeletedWithoutRecursion()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK,
            "<EnumerationResults><Blobs><Blob><Name>precious.txt</Name></Blob></Blobs><NextMarker /></EnumerationResults>"));
        var service = Service(fake);

        await AsyncAssert.ThrowsAsync<IOException>(() =>
        {
            service.DeleteFolder(new Url("azblob://acc/c/"), recursive: false, moveToRecycleBin: false);
            return Task.CompletedTask;
        });

        Assert.IsFalse(fake.Requests.Any(r => r.Method == HttpMethod.Delete), "No delete request was sent.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyFolder_IsDeletedByItsMarker()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.Accepted, string.Empty));
        var service = Service(fake);

        var removed = service.DeleteFolder(new Url("azblob://acc/c/docs"), recursive: false, moveToRecycleBin: false);

        Assert.IsTrue(removed);
        Assert.AreEqual("https://acc.blob.core.windows.net/c/docs/", fake.Requests.Single().Uri.ToString());
    }
}
