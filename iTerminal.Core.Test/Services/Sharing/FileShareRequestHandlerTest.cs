using System.Text;
using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Test.Services.Sharing;

[TestClass]
public class FileShareRequestHandlerTest
{
    private const string Token = "TestToken123";

    private string _folder = null!;
    private string _shared = null!;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminal-share-" + Guid.NewGuid().ToString("N"));
        _shared = Path.Combine(_folder, "shared");
        Directory.CreateDirectory(_shared);
        File.WriteAllText(Path.Combine(_shared, "notes.txt"), "hello share");
        File.WriteAllText(Path.Combine(_shared, ".hidden"), "dot");
        Directory.CreateDirectory(Path.Combine(_shared, "sub"));
        File.WriteAllText(Path.Combine(_shared, "sub", "inner.txt"), "inner");
        File.WriteAllText(Path.Combine(_folder, "outside.txt"), "secret");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Request_WithoutTheToken_IsNotFound()
    {
        Response response = await Send("GET", "/notes.txt");

        Assert.AreEqual(404, response.Status);
    }

    [TestMethod]
    public async Task Request_WithAWrongToken_IsNotFound()
    {
        Response response = await Send("GET", "/TestToken124/shared/notes.txt");

        Assert.AreEqual(404, response.Status);
    }

    [TestMethod]
    public async Task Request_WithAnEmptyToken_IsNotFound()
    {
        Response response = await Send("GET", "//shared/notes.txt");

        Assert.AreEqual(404, response.Status);
    }

    [TestMethod]
    public async Task Listing_ShowsTheSelectionAndHidesNothing()
    {
        Response response = await Send("GET", $"/{Token}/");

        Assert.AreEqual(200, response.Status);
        string body = Encoding.UTF8.GetString(response.Body);
        Assert.IsTrue(body.Contains(">shared</a>", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("outside.txt", StringComparison.Ordinal), "only the selection is listed");
    }

    [TestMethod]
    public async Task Listing_OfAFolder_ShowsDotfilesAndEscapesNames()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows does not allow this character in a file name.");
        }

        File.WriteAllText(Path.Combine(_shared, "a<b>.txt"), "x");

        Response response = await Send("GET", $"/{Token}/shared/");

        Assert.AreEqual(200, response.Status);
        string body = Encoding.UTF8.GetString(response.Body);
        Assert.IsTrue(body.Contains(">.hidden</a>", StringComparison.Ordinal), "dotfiles are listed");
        Assert.IsTrue(body.Contains("a&lt;b&gt;.txt", StringComparison.Ordinal), "link text is HTML-escaped");
        Assert.IsFalse(body.Contains("a<b>.txt", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Listing_LinksEncodeEachSegment()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows does not allow this character in a file name.");
        }

        File.WriteAllText(Path.Combine(_shared, "a\"b.txt"), "x");

        Response response = await Send("GET", $"/{Token}/shared/");

        string body = Encoding.UTF8.GetString(response.Body);
        Assert.IsTrue(body.Contains($"href=\"/{Token}/shared/a%22b.txt\"", StringComparison.Ordinal), "the href is URL-encoded");
        Assert.IsFalse(body.Contains("href=\"/" + Token + "/shared/a\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Download_ReturnsTheBytesAsAnAttachment()
    {
        Response response = await Send("GET", $"/{Token}/shared/notes.txt");

        Assert.AreEqual(200, response.Status);
        Assert.AreEqual("hello share", Encoding.UTF8.GetString(response.Body));
        Assert.IsTrue(response.Head.Contains("Content-Disposition: attachment; filename*=UTF-8''notes.txt", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Head_ReturnsTheHeadersWithoutTheBody()
    {
        Response response = await Send("HEAD", $"/{Token}/shared/notes.txt");

        Assert.AreEqual(200, response.Status);
        Assert.AreEqual(0, response.Body.Length);
        Assert.IsTrue(response.Head.Contains("Content-Length: 11", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("/{t}/shared/../outside.txt")]
    [DataRow("/{t}/shared/%2e%2e/outside.txt")]
    [DataRow("/{t}/shared/..%2foutside.txt")]
    [DataRow("/{t}/shared/%2fnotes.txt")]
    [DataRow("/{t}/shared/%5cnotes.txt")]
    [DataRow("/{t}/shared/%00notes.txt")]
    public async Task Traversal_IsNotFound(string target)
    {
        Response response = await Send("GET", target.Replace("{t}", Token, StringComparison.Ordinal));

        Assert.AreEqual(404, response.Status);
        Assert.IsFalse(Encoding.UTF8.GetString(response.Body).Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Target_WithABackslash_IsBadRequest()
    {
        Response response = await Send("GET", $"/{Token}/shared\\notes.txt");

        Assert.AreEqual(400, response.Status);
    }

    [TestMethod]
    public async Task Unselected_Name_IsNotFound()
    {
        Response response = await Send("GET", $"/{Token}/outside.txt");

        Assert.AreEqual(404, response.Status);
    }

    [TestMethod]
    public async Task Symlink_IsNotFollowed_AndIsNotListed()
    {
        string link = Path.Combine(_shared, "escape.txt");
        if (!TryCreateSymlink(link, Path.Combine(_folder, "outside.txt")))
        {
            Assert.Inconclusive("Creating a symbolic link needs privilege on this machine.");
        }

        Response file = await Send("GET", $"/{Token}/shared/escape.txt");
        Response listing = await Send("GET", $"/{Token}/shared/");

        Assert.AreEqual(404, file.Status);
        Assert.IsFalse(Encoding.UTF8.GetString(listing.Body).Contains("escape.txt", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Symlinked_IntermediateFolder_IsNotFollowed()
    {
        string link = Path.Combine(_shared, "linkdir");
        if (!TryCreateSymlink(link, _folder))
        {
            Assert.Inconclusive("Creating a symbolic link needs privilege on this machine.");
        }

        Response response = await Send("GET", $"/{Token}/shared/linkdir/outside.txt");

        Assert.AreEqual(404, response.Status);
    }

    [TestMethod]
    [DataRow("POST")]
    [DataRow("PUT")]
    [DataRow("DELETE")]
    public async Task OtherMethods_AreNotAllowed(string method)
    {
        Response response = await Send(method, $"/{Token}/shared/notes.txt");

        Assert.AreEqual(405, response.Status);
        Assert.IsTrue(response.Head.Contains("Allow: GET, HEAD", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Reads_OverTheCap_AreAnsweredServiceUnavailable()
    {
        FileShareRequestHandler handler = new([_shared], Token);
        for (int i = 0; i < 4; i++)
        {
            Assert.IsTrue(handler.ReadSlots.Wait(0));
        }

        using MemoryStream output = new();
        int status = await handler.WriteResponseAsync("GET", $"/{Token}/shared/notes.txt", output, CancellationToken.None);

        Assert.AreEqual(503, status);
        Assert.IsTrue(Encoding.ASCII.GetString(output.ToArray()).Contains("Retry-After: 1", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ErrorWriter_WritesTheStatusOnly()
    {
        FileShareRequestHandler handler = new([_shared], Token);
        using MemoryStream output = new();

        await FileShareRequestHandler.WriteErrorAsync(output, 413, CancellationToken.None);

        Assert.IsTrue(Encoding.ASCII.GetString(output.ToArray()).StartsWith("HTTP/1.1 413 ", StringComparison.Ordinal));
    }

    private async Task<Response> Send(string method, string target)
    {
        FileShareRequestHandler handler = new([_shared], Token);
        using MemoryStream output = new();
        await handler.WriteResponseAsync(method, target, output, CancellationToken.None);
        return Response.Parse(output.ToArray());
    }

    private static bool TryCreateSymlink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record Response(int Status, string Head, byte[] Body)
    {
        public static Response Parse(byte[] bytes)
        {
            byte[] separator = "\r\n\r\n"u8.ToArray();
            int split = -1;
            for (int i = 0; i + separator.Length <= bytes.Length; i++)
            {
                if (bytes.AsSpan(i, separator.Length).SequenceEqual(separator))
                {
                    split = i;
                    break;
                }
            }

            string head = Encoding.ASCII.GetString(bytes, 0, split);
            byte[] body = bytes[(split + separator.Length)..];
            int status = int.Parse(head.Split(' ')[1]);
            return new Response(status, head, body);
        }
    }
}
