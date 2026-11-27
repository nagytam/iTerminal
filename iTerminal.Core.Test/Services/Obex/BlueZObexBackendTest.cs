using iTerminal.Core.Services.Obex;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Obex.Test;

/// <summary>The busctl calls the OBEX backend makes, and the answers it reads, recorded rather than run.</summary>
[TestClass]
public class BlueZObexBackendTest
{
    /// <summary>A busctl stand-in: records each call and answers it from a script.</summary>
    private sealed class ScriptedBus(Func<IReadOnlyList<string>, ProcessOutput> answer) : IExternalProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(string.Join(' ', arguments));
            return Task.FromResult(answer(arguments));
        }

        public Task<ProcessOutput> RunToStreamAsync(string fileName, IReadOnlyList<string> arguments, Stream destination, string? workingDirectory = null, Func<Stream, Task>? writeStandardInput = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private const string Session = "/org/bluez/obex/client/session0";
    private const string Address = "AA:BB:CC:DD:EE:FF";

    private static ProcessOutput Ok(string json = "") => new(0, json, string.Empty);

    private static ProcessOutput Failed(string message) => new(1, string.Empty, message);

    /// <summary>The answers a phone that browses fine gives: a session, folder changes, a listing, a removed session.</summary>
    private static ProcessOutput Answer(IReadOnlyList<string> arguments)
    {
        if (arguments.Contains("CreateSession"))
        {
            return Ok($"{{\"type\":\"o\",\"data\":\"{Session}\"}}");
        }

        if (arguments.Contains("ListFolder"))
        {
            return Ok("""{"type":"aa{sv}","data":[[{"Name":{"type":"s","data":"DCIM"},"Type":{"type":"s","data":"folder"},"Size":{"type":"t","data":0},"Modified":{"type":"s","data":"20240102T101500"}},{"Name":{"type":"s","data":"a.jpg"},"Type":{"type":"s","data":"file"},"Size":{"type":"t","data":2048},"Modified":{"type":"s","data":"20240103T120000"}},{"Name":{"type":"s","data":"."},"Type":{"type":"s","data":"folder"}}]]}""");
        }

        return Ok();
    }

    [TestMethod]
    public void ParseListing_ReadsTheNestedDictionaries_AndDropsTheDotRows()
    {
        var rows = BlueZObexBackend.ParseListing("""{"type":"aa{sv}","data":[[{"Name":{"type":"s","data":"DCIM"},"Type":{"type":"s","data":"folder"},"Size":{"type":"t","data":0},"Modified":{"type":"s","data":"20240102T101500"}},{"Name":{"type":"s","data":"."},"Type":{"type":"s","data":"folder"}}]]}""");

        var row = rows.Single();
        Assert.AreEqual("DCIM", row.Name);
        Assert.IsTrue(row.IsFolder);
        Assert.AreEqual(new DateTime(2024, 1, 2, 10, 15, 0), row.Modified);
    }

    [TestMethod]
    public void ParseListing_ReadsAFlatArrayToo()
    {
        var rows = BlueZObexBackend.ParseListing("""{"type":"aa{sv}","data":[{"Name":{"type":"s","data":"a.jpg"},"Type":{"type":"s","data":"file"},"Size":{"type":"t","data":2048}}]}""");

        var row = rows.Single();
        Assert.AreEqual("a.jpg", row.Name);
        Assert.IsFalse(row.IsFolder);
        Assert.AreEqual(2048L, row.Size);
    }

    [TestMethod]
    public void ParseObjectPath_ReadsAPlainAnswer_AndTheFirstOfSeveral()
    {
        Assert.AreEqual(Session, BlueZObexBackend.ParseObjectPath($"{{\"type\":\"o\",\"data\":\"{Session}\"}}"));
        Assert.AreEqual(Session + "/transfer0", BlueZObexBackend.ParseObjectPath($"{{\"type\":\"oa{{sv}}\",\"data\":[\"{Session}/transfer0\",[]]}}"));
    }

    [TestMethod]
    public void ParseModified_ReadsObexdsFormat_AndAnIsoDate()
    {
        Assert.AreEqual(new DateTime(2024, 1, 3, 12, 0, 0), BlueZObexBackend.ParseModified("20240103T120000"));
        Assert.IsNull(BlueZObexBackend.ParseModified(null));
        Assert.IsNotNull(BlueZObexBackend.ParseModified("2024-01-03T12:00:00Z"));
    }

    [TestMethod]
    public void Explain_NamesAMissingObexd_AndKeepsTheLastErrorName()
    {
        Assert.AreEqual(
            "BlueZ's file transfer service (obexd) is not installed or not running",
            BlueZObexBackend.Explain("Failed to start transient service unit: The name org.bluez.obex was not provided by any .service files"));
        Assert.AreEqual("Connection refused", BlueZObexBackend.Explain("Failed to call CreateSession: org.bluez.obex.Error.Failed: Connection refused"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListAsync_OpensASession_WalksTheFolder_ListsIt_AndRemovesTheSession()
    {
        var bus = new ScriptedBus(Answer);
        var backend = new BlueZObexBackend(bus);

        var rows = await backend.ListAsync(Address, "Music/Rock", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "DCIM", "a.jpg" }, rows.Select(r => r.Name).ToArray());
        Assert.IsTrue(bus.Calls[0].Contains("CreateSession") && bus.Calls[0].Contains(Address), "The session is for the device, the ftp target.");
        Assert.IsTrue(bus.Calls[0].Contains("Target s ftp"));
        Assert.IsTrue(bus.Calls[1].EndsWith("ChangeFolder s Music", StringComparison.Ordinal));
        Assert.IsTrue(bus.Calls[2].EndsWith("ChangeFolder s Rock", StringComparison.Ordinal));
        Assert.IsTrue(bus.Calls[3].EndsWith("ListFolder", StringComparison.Ordinal));
        Assert.IsTrue(bus.Calls[4].Contains("RemoveSession o " + Session), "A session is never left open.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Download_ReadsTheTemporaryFileObexdWrote_AndDeletesIt()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        string? written = null;
        var bus = new ScriptedBus(arguments =>
        {
            if (arguments.Contains("GetFile"))
            {
                written = arguments[^2];
                File.WriteAllBytes(written, content);
                return Ok($"{{\"type\":\"oa{{sv}}\",\"data\":[\"{Session}/transfer0\",[]]}}");
            }

            if (arguments.Contains("Status"))
            {
                return Ok("""{"type":"s","data":"complete"}""");
            }

            return Answer(arguments);
        });
        var backend = new BlueZObexBackend(bus, pollInterval: TimeSpan.FromMilliseconds(1));
        using var destination = new MemoryStream();

        await backend.DownloadAsync(Address, "Music/a.mp3", destination, null, CancellationToken.None);

        CollectionAssert.AreEqual(content, destination.ToArray());
        Assert.IsTrue(bus.Calls.Any(c => c.EndsWith("GetFile ss " + written + " a.mp3", StringComparison.Ordinal)));
        Assert.IsFalse(File.Exists(written!), "The temporary file is removed.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATransferThatErrors_SaysSo()
    {
        var bus = new ScriptedBus(arguments =>
        {
            if (arguments.Contains("PutFile"))
            {
                return Ok($"{{\"type\":\"oa{{sv}}\",\"data\":[\"{Session}/transfer0\",[]]}}");
            }

            if (arguments.Contains("Status"))
            {
                return Ok("""{"type":"s","data":"error"}""");
            }

            return Answer(arguments);
        });
        var backend = new BlueZObexBackend(bus, pollInterval: TimeSpan.FromMilliseconds(1));

        var error = await AsyncAssert.ThrowsAsync<IOException>(() => backend.UploadAsync(Address, "Music/new.mp3", new MemoryStream([1]), null, CancellationToken.None));

        StringAssert.Contains(error.Message, "the device stopped the transfer");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAddressThatIsNotOne_IsRefusedBeforeAnyCall()
    {
        var bus = new ScriptedBus(Answer);
        var backend = new BlueZObexBackend(bus);

        var error = await AsyncAssert.ThrowsAsync<IOException>(() => backend.ListAsync("AA:BB; rm -rf", string.Empty, CancellationToken.None));

        StringAssert.Contains(error.Message, "Not a Bluetooth address");
        Assert.AreEqual(0, bus.Calls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutBusctl_TheErrorNamesIt()
    {
        var backend = new BlueZObexBackend(new ScriptedBus(_ => new ProcessOutput(-1, string.Empty, "No such file")));

        var error = await AsyncAssert.ThrowsAsync<IOException>(() => backend.ListAsync(Address, string.Empty, CancellationToken.None));

        StringAssert.Contains(error.Message, "busctl");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedCall_IsSaidInWordsFromTheBus()
    {
        var backend = new BlueZObexBackend(new ScriptedBus(_ => Failed("Failed to call CreateSession: org.bluez.obex.Error.Failed: Connection refused")));

        var error = await AsyncAssert.ThrowsAsync<IOException>(() => backend.ListAsync(Address, string.Empty, CancellationToken.None));

        Assert.AreEqual("Bluetooth file transfer: Connection refused.", error.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreatingAFolderThatIsAlreadyOnThePhone_IsNotAnError()
    {
        var bus = new ScriptedBus(arguments => arguments.Contains("CreateFolder")
            ? Failed("Failed to call CreateFolder: org.bluez.obex.Error.AlreadyExists: File exists")
            : Answer(arguments));
        var backend = new BlueZObexBackend(bus);

        await backend.CreateFolderAsync(Address, "Music/New", CancellationToken.None);

        Assert.IsTrue(bus.Calls.Any(c => c.Contains("RemoveSession o " + Session)), "The session is still closed.");
    }
}
