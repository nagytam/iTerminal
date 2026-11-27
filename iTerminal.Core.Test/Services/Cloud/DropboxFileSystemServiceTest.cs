using System.Net;
using System.Text;
using System.Text.Json;
using iConsole.Data;
using iTerminal.Core.Services.Cloud;
using iTerminal.Core.Test.Helper;
using iTerminal.Core.Test.Services.Cloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Cloud.Test;

/// <summary>What the Dropbox drive asks of the API, and what it makes of the answers.</summary>
[TestClass]
public class DropboxFileSystemServiceTest
{
    /// <summary>Hands out a token that names the account it was asked for, and remembers the accounts.</summary>
    private sealed class RecordingTokens : ICloudTokenProvider
    {
        public List<string> Accounts { get; } = [];

        public Task<string> GetAccessTokenAsync(string account, CancellationToken cancellationToken)
        {
            Accounts.Add(account);
            return Task.FromResult("token-" + account);
        }

        public void Invalidate(string account)
        {
        }
    }

    private static DropboxFileSystemService Service(FakeHttpHandler fake, RecordingTokens tokens) =>
        new(tokens, fake, "https://api.test", "https://content.test");

    [Timeout(60_000)]
    [TestMethod]
    public async Task List_ReadsEveryPage_AndPutsFoldersFirst()
    {
        var fake = new FakeHttpHandler(seen =>
        {
            if (seen.Uri.AbsolutePath == "/2/files/list_folder")
            {
                return FakeHttpHandler.Reply(HttpStatusCode.OK, """{"entries":[{".tag":"file","name":"b.txt","size":5,"server_modified":"2024-01-02T10:00:00Z"},{".tag":"folder","name":"Docs"}],"has_more":true,"cursor":"c1"}""", "application/json");
            }

            return FakeHttpHandler.Reply(HttpStatusCode.OK, """{"entries":[{".tag":"file","name":"A.txt","size":7}],"has_more":false}""", "application/json");
        });
        var tokens = new RecordingTokens();
        var service = Service(fake, tokens);

        var rows = await service.ListAsync(new Url("dropbox://acc/"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Docs", "A.txt", "b.txt" }, rows.Select(r => r.Url.Name).ToArray());
        Assert.AreEqual(PathType.Folder, rows[0].Url.PathType);
        Assert.AreEqual(PathType.File, rows[2].Url.PathType);
        Assert.AreEqual("Bearer token-acc", fake.Requests[0].Header("Authorization"));
        Assert.AreEqual(2, fake.Requests.Count);
        StringAssert.Contains(fake.Requests[0].Text, "\"path\":\"\"", "The root of an account is the empty path.");
        StringAssert.Contains(fake.Requests[1].Text, "\"cursor\":\"c1\"", "The second page asks for the cursor of the first.");
        CollectionAssert.AreEqual(new[] { "acc", "acc" }, tokens.Accounts);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Exists_UsesGetMetadata_AndANotFoundIsAbsent()
    {
        var fake = new FakeHttpHandler(seen =>
        {
            if (seen.Text.Contains("\"/Docs\"", StringComparison.Ordinal))
            {
                return FakeHttpHandler.Reply(HttpStatusCode.OK, """{".tag":"folder","name":"Docs"}""", "application/json");
            }

            return FakeHttpHandler.Reply(HttpStatusCode.Conflict, """{"error_summary":"path/not_found/..."}""", "application/json");
        });
        var service = Service(fake, new RecordingTokens());

        Assert.IsTrue(service.ExistsFolder(new Url("dropbox://acc/Docs")));
        Assert.IsFalse(service.ExistsFile(new Url("dropbox://acc/Docs")), "A folder is not a file.");
        Assert.IsFalse(service.ExistsFile(new Url("dropbox://acc/missing.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Download_SendsThePathInTheArgumentHeader_WithNonAsciiEscaped()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, "hello", "application/octet-stream"));
        var service = Service(fake, new RecordingTokens());
        using var destination = new MemoryStream();

        await service.StreamToAsync(new Url("dropbox://acc/Fotos/café.txt"), destination, null, CancellationToken.None);

        var request = fake.Requests.Single();
        Assert.AreEqual("https://content.test/2/files/download", request.Uri.ToString());
        var argument = request.Header("Dropbox-API-Arg")!;
        StringAssert.Contains(argument.ToLowerInvariant(), "\\u00e9", "Dropbox wants the header ASCII-only.");
        Assert.AreEqual("/Fotos/café.txt", JsonDocument.Parse(argument).RootElement.GetProperty("path").GetString());
        Assert.AreEqual("hello", Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_OntoAnExistingName_DeletesTheTargetFirst()
    {
        var fake = new FakeHttpHandler(seen =>
        {
            if (seen.Uri.AbsolutePath == "/2/files/get_metadata")
            {
                return FakeHttpHandler.Reply(HttpStatusCode.OK, """{".tag":"file"}""", "application/json");
            }

            return FakeHttpHandler.Reply(HttpStatusCode.OK, "{}", "application/json");
        });
        var service = Service(fake, new RecordingTokens());

        await service.MoveAsync(new Url("dropbox://acc/a.txt"), new Url("dropbox://acc/target.txt"), overwrite: true);

        var paths = fake.Requests.Select(r => r.Uri.AbsolutePath).ToList();
        // The source is confirmed first, then the target is looked up and deleted, then the move runs.
        CollectionAssert.AreEqual(new[] { "/2/files/get_metadata", "/2/files/get_metadata", "/2/files/delete_v2", "/2/files/move_v2" }, paths);
        var move = JsonDocument.Parse(fake.Requests[3].Text).RootElement;
        Assert.AreEqual("/a.txt", move.GetProperty("from_path").GetString());
        Assert.AreEqual("/target.txt", move.GetProperty("to_path").GetString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task A_MissingFolder_ReportsItInWords()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.Conflict, """{"error_summary":"path/not_found/.."}""", "application/json"));
        var service = Service(fake, new RecordingTokens());

        var error = await AsyncAssert.ThrowsAsync<IOException>(() => service.ListAsync(new Url("dropbox://acc/Gone/"), CancellationToken.None));

        Assert.AreEqual("Dropbox listing: it is not there (path/not_found/..).", error.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAccountNamedInTheUrl_IsTheOneThatSigns()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, """{"entries":[],"has_more":false}""", "application/json"));
        var tokens = new RecordingTokens();
        var service = Service(fake, tokens);

        await service.ListAsync(new Url("dropbox://work/"), CancellationToken.None);
        await service.ListAsync(new Url("dropbox:///"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "work", DropboxTokenProvider.DefaultAccount }, tokens.Accounts);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task A_LargeFile_GoesThroughAnUploadSession()
    {
        var sessionCalls = new List<string>();
        var fake = new FakeHttpHandler(seen =>
        {
            sessionCalls.Add(seen.Uri.AbsolutePath);
            return seen.Uri.AbsolutePath switch
            {
                "/2/files/upload_session/start" => FakeHttpHandler.Reply(HttpStatusCode.OK, """{"session_id":"S1"}""", "application/json"),
                _ => FakeHttpHandler.Reply(HttpStatusCode.OK, "{}", "application/json"),
            };
        });
        var service = Service(fake, new RecordingTokens());
        var size = 150L * 1024 * 1024 + 1;
        using var source = new MemoryStream(new byte[size]);

        await service.StreamFromAsync(new Url("dropbox://acc/big.bin"), source, null, CancellationToken.None);

        Assert.AreEqual("/2/files/upload_session/start", sessionCalls.First());
        Assert.AreEqual("/2/files/upload_session/finish", sessionCalls.Last());
        StringAssert.Contains(fake.Requests.Last().Header("Dropbox-API-Arg")!, "\"session_id\":\"S1\"");
        StringAssert.Contains(fake.Requests.Last().Header("Dropbox-API-Arg")!, "\"path\":\"/big.bin\"");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACopyOntoItself_ChangesNothing()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.OK, "{}", "application/json"));
        var service = Service(fake, new RecordingTokens());

        await service.MoveAsync(new Url("dropbox://acc/a.txt"), new Url("dropbox://acc/a.txt"), overwrite: true);

        Assert.AreEqual(0, fake.Requests.Count, "Deleting the target would delete the only copy.");
    }
}
