using System.Diagnostics;
using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services.Lsp;

namespace iTerminal.DevelopmentStudio.Test.Services.Lsp;

[TestClass]
public class CSharpLanguageServerTest
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "CSharpStudio");
    private static readonly string ProgramPath = Path.Combine(Folder, "Program.cs");
    private static readonly string OtherPath = Path.Combine(Folder, "Other.cs");

    /// <summary>Publishes one error at line 3, character 4 for any text that contains "ERROR".</summary>
    private static JsonArray ErrorWhenMarked(string uri, string text) =>
        text.Contains("ERROR", StringComparison.Ordinal)
            ? [FakeLanguageServer.Diagnostic(2, 4, 1, "CS0029", "Cannot implicitly convert type")]
            : [];

    [Timeout(30_000)]
    [TestMethod]
    public async Task AnOpenFileGetsTheDiagnosticsTheServerReportsForIt()
    {
        var fake = new FakeLanguageServer { Diagnostics = ErrorWhenMarked };
        await using var server = new CSharpLanguageServer(fake);

        server.OpenFile(Folder, ProgramPath, "int x = ERROR;");
        await WaitUntilAsync(() => server.State == LanguageServerState.Ready && server.AllDiagnostics().Count == 1, "the diagnostic to arrive");

        LiveDiagnostic diagnostic = server.DiagnosticsFor(ProgramPath)[0];
        Assert.AreEqual(3, diagnostic.Line, "the server's 0-based line is shown 1-based");
        Assert.AreEqual(5, diagnostic.Column, "the server's 0-based character is shown 1-based");
        Assert.IsTrue(diagnostic.IsError);
        Assert.AreEqual("CS0029", diagnostic.Code);
        Assert.AreEqual("Cannot implicitly convert type", diagnostic.Message);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ASavedFileReachesTheServerWithItsNewTextAndItsDiagnosticsFollow()
    {
        var fake = new FakeLanguageServer { Diagnostics = ErrorWhenMarked };
        await using var server = new CSharpLanguageServer(fake);
        server.OpenFile(Folder, ProgramPath, "int x;");
        await WaitUntilAsync(() => server.State == LanguageServerState.Ready && fake.Methods.Contains("textDocument/didOpen"), "the file to be opened");

        server.SaveFile(Folder, ProgramPath, "int x = ERROR;");

        await WaitUntilAsync(() => server.DiagnosticsFor(ProgramPath).Count == 1, "the saved text's diagnostic to arrive");
        CollectionAssert.Contains(fake.Methods.ToList(), "textDocument/didChange");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ASaveOfAFileTheServerDoesNotHoldOpenIsNotSent()
    {
        var fake = new FakeLanguageServer { Diagnostics = ErrorWhenMarked };
        await using var server = new CSharpLanguageServer(fake);
        server.OpenFile(Folder, ProgramPath, "int x;");
        await WaitUntilAsync(() => server.State == LanguageServerState.Ready && fake.Methods.Contains("textDocument/didOpen"), "the file to be opened");

        server.SaveFile(Folder, OtherPath, "ERROR");

        await Task.Delay(200);
        Assert.IsFalse(fake.Methods.Contains("textDocument/didChange"));
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task TheFileThatStartsTheServerIsSentAfterInitializeAndOnlyOnce()
    {
        var fake = new FakeLanguageServer { Diagnostics = ErrorWhenMarked };
        await using var server = new CSharpLanguageServer(fake);

        // The first open starts the server, so the file is queued until the server has initialized.
        server.OpenFile(Folder, ProgramPath, "ERROR");
        await WaitUntilAsync(() => server.AllDiagnostics().Count == 1, "the diagnostic of the file that started the server");

        List<string> methods = [.. fake.Methods];
        Assert.AreEqual(1, methods.Count(method => method == "textDocument/didOpen"), "the file is opened exactly once");
        Assert.IsTrue(methods.IndexOf("initialized") < methods.IndexOf("textDocument/didOpen"), "didOpen is sent after initialized");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ADiagnosticForAFileTheClientNeverOpenedIsDropped()
    {
        var fake = new FakeLanguageServer
        {
            Diagnostics = ErrorWhenMarked,
            AlsoPublishFor = new Uri(OtherPath).AbsoluteUri,
        };
        await using var server = new CSharpLanguageServer(fake);

        server.OpenFile(Folder, ProgramPath, "ERROR");
        await WaitUntilAsync(() => server.AllDiagnostics().Count == 1, "the diagnostic of the open file");
        await Task.Delay(100);

        Assert.AreEqual(1, server.AllDiagnostics().Count, "the server's diagnostic for the file that was not opened is dropped");
        Assert.AreEqual(0, server.DiagnosticsFor(OtherPath).Count);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AServerThatIsNotInstalledIsReportedAsMissing()
    {
        var fake = new FakeLanguageServer { Program = null };
        await using var server = new CSharpLanguageServer(fake);

        server.OpenFile(Folder, ProgramPath, "int x;");
        await WaitUntilAsync(() => server.State == LanguageServerState.Missing, "the server to report it is missing");

        Assert.AreEqual(0, fake.Launches, "nothing is started when the server is not installed");
        Assert.AreEqual(0, server.AllDiagnostics().Count);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AChangeOfFolderDropsTheDiagnosticsOfTheOldFolderAtOnce()
    {
        var fake = new FakeLanguageServer { Diagnostics = ErrorWhenMarked };
        await using var server = new CSharpLanguageServer(fake);
        server.OpenFile(Folder, ProgramPath, "ERROR");
        await WaitUntilAsync(() => server.AllDiagnostics().Count == 1, "the diagnostic in the first folder");

        string otherFolder = Path.Combine(Path.GetTempPath(), "CSharpOther");
        server.OpenFile(otherFolder, Path.Combine(otherFolder, "Program.cs"), "int y;");

        Assert.AreEqual(0, server.AllDiagnostics().Count, "the old folder's diagnostics are gone as soon as the folder changes");
        Assert.AreEqual(otherFolder, server.Folder);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AFolderChangeReturnsWithoutWaitingForTheOldServerToShutDown()
    {
        var fake = new FakeLanguageServer { RespondToShutdown = false };
        await using var server = new CSharpLanguageServer(fake, TimeSpan.FromSeconds(3));
        server.OpenFile(Folder, ProgramPath, "int x;");
        await WaitUntilAsync(() => server.State == LanguageServerState.Ready, "the first server to be ready");

        var clock = Stopwatch.StartNew();
        server.OpenFile(Path.Combine(Path.GetTempPath(), "CSharpOther"), Path.Combine(Path.GetTempPath(), "CSharpOther", "Program.cs"), "int y;");
        clock.Stop();

        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(1), $"the caller waited {clock.Elapsed.TotalMilliseconds} ms; the shutdown bound is 3 s, and the key handler must not wait for it");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task DisposeKillsASilentServerWithinTheShutdownBound()
    {
        var fake = new FakeLanguageServer { RespondToShutdown = false };
        var server = new CSharpLanguageServer(fake, TimeSpan.FromMilliseconds(100));
        server.OpenFile(Folder, ProgramPath, "int x;");
        await WaitUntilAsync(() => server.State == LanguageServerState.Ready, "the server to be ready");

        var clock = Stopwatch.StartNew();
        await server.DisposeAsync();
        clock.Stop();

        Assert.IsTrue(fake.Killed, "the process is killed when the shutdown does not answer");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(3), $"dispose took {clock.Elapsed.TotalMilliseconds} ms, but the bound is 100 ms");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) Assert.Fail($"Timed out waiting for {what}.");
            await Task.Delay(10);
        }
    }
}
