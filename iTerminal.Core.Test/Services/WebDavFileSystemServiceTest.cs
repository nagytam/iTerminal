using System.Net;
using System.Text;
using iConsole.Data;
using iTerminal.Core.Services;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class WebDavFileSystemServiceTest
{
    private const string Listing = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:multistatus xmlns:d="DAV:">
          <d:response>
            <d:href>/docs/</d:href>
            <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat>
          </d:response>
          <d:response>
            <d:href>/docs/my%20notes%23.txt</d:href>
            <d:propstat>
              <d:prop><d:resourcetype/><d:getcontentlength>2048</d:getcontentlength><d:getlastmodified>Mon, 05 Oct 2026 09:30:00 GMT</d:getlastmodified></d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>http://example.org/docs/Zeta/</d:href>
            <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat>
          </d:response>
          <d:response>
            <d:href>/docs/alpha.md</d:href>
            <d:propstat><d:prop><d:resourcetype/><d:getcontentlength>10</d:getcontentlength></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat>
            <d:propstat><d:prop><d:getlastmodified/></d:prop><d:status>HTTP/1.1 404 Not Found</d:status></d:propstat>
          </d:response>
        </d:multistatus>
        """;

    private sealed record Seen(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

    private sealed class FakeHandler(Func<Seen, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var seen = new Seen(request.Method, request.RequestUri!, headers, body);
            lock (Requests)
            {
                Requests.Add(seen);
            }

            return respond(seen);
        }
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };

    private static (WebDavFileSystemService Service, FakeHandler Handler, string[] Login) Create(Func<Seen, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var login = new string[2];
        var service = new WebDavFileSystemService((user, password) =>
        {
            login[0] = user;
            login[1] = password;
            return handler;
        });
        return (service, handler, login);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Escape_EncodesEverySegmentAndKeepsTheTrailingSlash()
    {
        Assert.AreEqual("/", WebDavFileSystemService.Escape("/"));
        Assert.AreEqual("/a%20b/c%23d%25/", WebDavFileSystemService.Escape("/a b/c#d%/"));
        Assert.AreEqual("/x/y.txt", WebDavFileSystemService.Escape("/x/y.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Connect_HandsTheLoginToTheClient_AndSelectsTheSchemeFromTheUrl()
    {
        var (service, handler, login) = Create(_ => Reply(HttpStatusCode.NoContent));
        using var owned = service;

        service.Connect("example.org:8443", secure: true, "alice", "s3cret");
        service.DeleteFile(new Url("davs://alice@example.org:8443/a.txt"), moveToRecycleBin: false);

        Assert.AreEqual("alice", login[0]);
        Assert.AreEqual("s3cret", login[1]);
        Assert.IsTrue(service.IsConnected);
        Assert.AreEqual("https://example.org:8443/a.txt", handler.Requests[0].Uri.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void List_ReadsTheMultiStatus_FoldersFirst_WithDecodedNames()
    {
        var (service, handler, _) = Create(_ => Reply((HttpStatusCode)207, Listing));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        var entries = service.List(new Url("dav://example.org/docs/")).ToList();

        // ".." leads, then the folder, then the files by name; the folder's own row is dropped.
        CollectionAssert.AreEqual(
            new[] { "..", "Zeta", "alpha.md", "my notes#.txt" },
            entries.Select(e => e.Url.DisplayName).ToArray());
        Assert.AreEqual("dav://example.org/docs/my notes#.txt", entries[3].Url.FullUrl);
        Assert.AreEqual("dav://example.org/docs/Zeta/", entries[1].Url.FullUrl);
        Assert.AreEqual(PathType.Folder, entries[1].Url.PathType);
        Assert.AreEqual("PROPFIND", handler.Requests[0].Method.Method);
        Assert.AreEqual("1", handler.Requests[0].Headers["Depth"]);
        Assert.AreEqual("http://example.org/docs/", handler.Requests[0].Uri.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void List_AtTheRoot_HasNoParentRow()
    {
        var (service, _, _) = Create(_ => Reply((HttpStatusCode)207, Listing.Replace("/docs/", "/")));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        var entries = service.List(new Url("dav://example.org/")).ToList();

        Assert.IsFalse(entries.Any(e => e.Url.DisplayName == ".."));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListChildren_OfAFolderThatIsNotThere_Throws_SoAWalkDoesNotMistakeItForEmpty()
    {
        var (service, _, _) = Create(_ => Reply(HttpStatusCode.NotFound));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        Assert.Throws<IOException>(() => service.ListChildren(new Url("dav://example.org/missing/")).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void List_OfAFolderThatIsNotThere_IsEmpty_AndAFailureIsNotThrown()
    {
        var (service, _, _) = Create(_ => Reply(HttpStatusCode.NotFound));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        Assert.AreEqual(0, service.List(new Url("dav://example.org/missing/")).Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ExistsFile_AndExistsFolder_TellACollectionFromAFile()
    {
        var (service, _, _) = Create(request => request.Uri.AbsolutePath.EndsWith('/')
            ? Reply((HttpStatusCode)207, "<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/f/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>")
            : Reply((HttpStatusCode)207, "<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/f.txt</d:href><d:propstat><d:prop><d:resourcetype/></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>"));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        Assert.IsTrue(service.ExistsFolder(new Url("dav://example.org/f/")));
        Assert.IsFalse(service.ExistsFile(new Url("dav://example.org/f/")));
        Assert.IsTrue(service.ExistsFile(new Url("dav://example.org/f.txt")));
        Assert.IsFalse(service.ExistsFolder(new Url("dav://example.org/f.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Write_Read_AndCreateFolder_UseTheirVerbs()
    {
        var stored = string.Empty;
        var (service, handler, _) = Create(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                stored = request.Body;
                return Reply(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Get)
            {
                return Reply(HttpStatusCode.OK, stored);
            }

            return Reply(request.Method.Method == "MKCOL" ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.BadRequest);
        });
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);
        var url = new Url("dav://example.org/docs/a b.txt");

        await service.WriteFileAsync(url, new MemoryStream(Encoding.UTF8.GetBytes("hello")));
        var file = await service.ReadFileAsync(url);
        await service.CreateFolderAsync(new Url("dav://example.org/docs/new/"));

        Assert.AreEqual("hello", Encoding.UTF8.GetString(file!.Content!));
        Assert.AreEqual("http://example.org/docs/a%20b.txt", handler.Requests[0].Uri.AbsoluteUri);
        Assert.AreEqual("MKCOL", handler.Requests[2].Method.Method, "an existing folder (405) is not an error");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write_RefusedByTheServer_SaysWhy()
    {
        var (service, _, _) = Create(_ => Reply(HttpStatusCode.Unauthorized));
        using var owned = service;
        service.Connect("example.org", secure: false, "alice", null);

        string? message = null;
        try
        {
            service.WriteFileAsync(new Url("dav://alice@example.org/x.txt"), new MemoryStream([1])).GetAwaiter().GetResult();
        }
        catch (IOException exception)
        {
            message = exception.Message;
        }

        Assert.IsNotNull(message, "a refused upload throws");
        StringAssert.Contains(message, "login");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Delete_ReportsWhetherThereWasSomethingToDelete()
    {
        var (service, handler, _) = Create(request => request.Uri.AbsolutePath.Contains("gone")
            ? Reply(HttpStatusCode.NotFound)
            : Reply(HttpStatusCode.NoContent));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        Assert.IsTrue(service.DeleteFile(new Url("dav://example.org/here.txt"), moveToRecycleBin: true));
        Assert.IsFalse(service.DeleteFile(new Url("dav://example.org/gone.txt"), moveToRecycleBin: false));
        Assert.IsTrue(service.DeleteFolder(new Url("dav://example.org/docs"), recursive: true, moveToRecycleBin: false) is true);
        Assert.IsTrue(handler.Requests[2].Uri.AbsolutePath.EndsWith('/'), "a collection is addressed with its trailing slash");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_AndCopy_AreDoneByTheServer_WithDestinationAndOverwrite()
    {
        var (service, handler, _) = Create(_ => Reply(HttpStatusCode.Created));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        await service.MoveAsync(new Url("dav://example.org/a.txt"), new Url("dav://example.org/new dir/b.txt"), overwrite: false);
        await service.CopyAsync(new Url("dav://example.org/a.txt"), new Url("dav://example.org/c.txt"), overwrite: true);

        Assert.AreEqual("MOVE", handler.Requests[0].Method.Method);
        Assert.AreEqual("http://example.org/new%20dir/b.txt", handler.Requests[0].Headers["Destination"]);
        Assert.AreEqual("F", handler.Requests[0].Headers["Overwrite"]);
        Assert.AreEqual("COPY", handler.Requests[1].Method.Method);
        Assert.AreEqual("T", handler.Requests[1].Headers["Overwrite"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_ThatWouldOverwrite_IsLeftAlone_NotAnError()
    {
        var (service, _, _) = Create(_ => Reply(HttpStatusCode.PreconditionFailed));
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);

        await service.MoveAsync(new Url("dav://example.org/a.txt"), new Url("dav://example.org/b.txt"), overwrite: false);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Streams_ReportProgress_BothWays()
    {
        var uploaded = string.Empty;
        var (service, _, _) = Create(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                uploaded = request.Body;
                return Reply(HttpStatusCode.NoContent);
            }

            return Reply(HttpStatusCode.OK, "0123456789");
        });
        using var owned = service;
        service.Connect("example.org", secure: false, null, null);
        var down = new List<long>();
        var up = new List<long>();

        var destination = new MemoryStream();
        await service.StreamToAsync(new Url("dav://example.org/f.bin"), destination, new Progress<long>(down.Add), CancellationToken.None);
        await service.StreamFromAsync(new Url("dav://example.org/g.bin"), new MemoryStream(Encoding.UTF8.GetBytes("abcdef")), new Progress<long>(up.Add), CancellationToken.None);
        await Task.Delay(50); // Progress<T> posts to the pool.

        Assert.AreEqual("0123456789", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.AreEqual("abcdef", uploaded);
        Assert.AreEqual(10, down.Max());
        Assert.AreEqual(6, up.Max());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Connect_ToAnotherUser_ReplacesTheClient()
    {
        var created = 0;
        var service = new WebDavFileSystemService((_, _) =>
        {
            created++;
            return new FakeHandler(_ => Reply(HttpStatusCode.NoContent));
        });
        using var owned = service;

        service.Connect("example.org", secure: false, "alice", "x");
        service.Connect("example.org", secure: false, "alice", "x");
        service.Connect("example.org", secure: false, "bob", "y");

        Assert.AreEqual(2, created);
    }
}
