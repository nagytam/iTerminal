using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Lsp;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Lsp;

[TestClass]
public class LspClientTest
{
    private const string Uri = "file:///work/app.cs";

    /// <summary>A pair of in-process pipes: what is written to <c>Write</c> is read from <c>Read</c>.</summary>
    private static (Stream Write, Stream Read) Pipe()
    {
        var server = new AnonymousPipeServerStream(PipeDirection.Out);
        var client = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle);
        return (server, client);
    }

    /// <summary>A language server in the test: the other end of the connection, answering what a real one would.</summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly JsonRpcConnection _connection;
        public List<string> Received { get; } = [];
        public string Text { get; private set; } = string.Empty;
        public bool ShutdownSeen { get; private set; }
        public bool ExitSeen { get; private set; }
        public bool Hover { get; init; } = true;

        public FakeServer(Stream input, Stream output)
        {
            _connection = new JsonRpcConnection(input, output);
            _connection.OnRequest("initialize", _ => Task.FromResult<object?>(new Dictionary<string, object?>
            {
                ["capabilities"] = new Dictionary<string, object?>
                {
                    ["hoverProvider"] = Hover,
                    ["completionProvider"] = new Dictionary<string, object?>(),
                    ["definitionProvider"] = true,
                },
                ["serverInfo"] = new Dictionary<string, object?> { ["name"] = "fake-ls" },
            }));
            _connection.OnRequest("shutdown", _ =>
            {
                ShutdownSeen = true;
                return Task.FromResult<object?>(null);
            });
            _connection.OnNotification("initialized", _ => Received.Add("initialized"));
            _connection.OnNotification("exit", _ => ExitSeen = true);
            _connection.OnNotification("textDocument/didOpen", p =>
            {
                Received.Add("didOpen");
                Text = p!.Value.GetProperty("textDocument").GetProperty("text").GetString()!;
                Publish();
            });
            _connection.OnNotification("textDocument/didChange", p =>
            {
                Received.Add("didChange");
                Text = p!.Value.GetProperty("contentChanges")[0].GetProperty("text").GetString()!;
                Publish();
            });
            _connection.OnNotification("textDocument/didClose", _ => Received.Add("didClose"));
            _connection.OnRequest("textDocument/hover", _ => Task.FromResult<object?>(new Dictionary<string, object?>
            {
                ["contents"] = new Dictionary<string, object?> { ["kind"] = "markdown", ["value"] = "**int** x" },
            }));
            _connection.OnRequest("textDocument/completion", _ => Task.FromResult<object?>(new Dictionary<string, object?>
            {
                ["isIncomplete"] = false,
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["label"] = "Console", ["insertText"] = "Console", ["kind"] = 7, ["detail"] = "class" },
                    new Dictionary<string, object?> { ["label"] = "WriteLine", ["kind"] = 2 },
                },
            }));
            _connection.OnRequest("textDocument/definition", _ => Task.FromResult<object?>(new object[]
            {
                new Dictionary<string, object?>
                {
                    ["uri"] = "file:///work/other.cs",
                    ["range"] = Range(4, 2, 4, 9),
                },
            }));
            _connection.Start();
        }

        public static Dictionary<string, object?> Range(int sl, int sc, int el, int ec) => new()
        {
            ["start"] = new Dictionary<string, object?> { ["line"] = sl, ["character"] = sc },
            ["end"] = new Dictionary<string, object?> { ["line"] = el, ["character"] = ec },
        };

        /// <summary>An error for each line that says BAD, a warning for each that says TODO, a range that spans two lines for LONG.</summary>
        private void Publish()
        {
            var diagnostics = new List<object>();
            string[] lines = Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int bad = lines[i].IndexOf("BAD", StringComparison.Ordinal);
                if (bad >= 0)
                {
                    diagnostics.Add(new Dictionary<string, object?>
                    {
                        ["range"] = Range(i, bad, i, bad + 3), ["severity"] = 1, ["message"] = "bad thing", ["source"] = "fake", ["code"] = 42,
                    });
                }

                int todo = lines[i].IndexOf("TODO", StringComparison.Ordinal);
                if (todo >= 0)
                {
                    diagnostics.Add(new Dictionary<string, object?>
                    {
                        ["range"] = Range(i, todo, i, todo + 4), ["severity"] = 2, ["message"] = "unfinished",
                    });
                }

                if (lines[i].Contains("LONG", StringComparison.Ordinal) && i + 1 < lines.Length)
                {
                    diagnostics.Add(new Dictionary<string, object?>
                    {
                        ["range"] = Range(i, 2, i + 1, 3), ["severity"] = 3, ["message"] = "spans lines",
                    });
                }
            }

            _connection.Notify("textDocument/publishDiagnostics", new Dictionary<string, object?> { ["uri"] = Uri, ["diagnostics"] = diagnostics });
        }

        public void Dispose() => _connection.Dispose();
    }

    private static (LspClient Client, FakeServer Server) Connect(bool hover = true)
    {
        (Stream clientWrites, Stream serverReads) = Pipe();
        (Stream serverWrites, Stream clientReads) = Pipe();
        var server = new FakeServer(serverReads, serverWrites) { Hover = hover };
        var client = new LspClient(new JsonRpcConnection(clientReads, clientWrites), TimeSpan.FromSeconds(5));
        return (client, server);
    }

    private static async Task WaitFor(Func<bool> condition, string what = "the condition")
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "Timed out waiting for " + what);
            await Task.Delay(10);
        }
    }

    // ---- JSON-RPC ----

    [TestMethod]
    public async Task ARequestIsMatchedToItsAnswer_AndANotificationReachesItsHandler()
    {
        (Stream aWrites, Stream bReads) = Pipe();
        (Stream bWrites, Stream aReads) = Pipe();
        using var a = new JsonRpcConnection(aReads, aWrites);
        using var b = new JsonRpcConnection(bReads, bWrites);
        string? heard = null;
        b.OnRequest("add", p => Task.FromResult<object?>(p!.Value.GetProperty("a").GetInt32() + p.Value.GetProperty("b").GetInt32()));
        b.OnNotification("note", p => heard = p!.Value.GetProperty("text").GetString());
        a.Start();
        b.Start();

        JsonElement? sum = await a.RequestAsync("add", new { a = 2, b = 40 }, TimeSpan.FromSeconds(5));
        a.Notify("note", new { text = "hello" });
        await WaitFor(() => heard is not null, "the notification");

        Assert.AreEqual(42, sum!.Value.GetInt32());
        Assert.AreEqual("hello", heard);
    }

    [TestMethod]
    public async Task AnErrorAnswer_ThrowsWithItsCodeAndMessage_AndAnUnknownMethodIsAnError()
    {
        (Stream aWrites, Stream bReads) = Pipe();
        (Stream bWrites, Stream aReads) = Pipe();
        using var a = new JsonRpcConnection(aReads, aWrites);
        using var b = new JsonRpcConnection(bReads, bWrites);
        b.OnRequest("boom", _ => throw new InvalidOperationException("it broke"));
        a.Start();
        b.Start();

        JsonRpcException broke = await Assert.ThrowsAsync<JsonRpcException>(async () => await a.RequestAsync("boom", null, TimeSpan.FromSeconds(5)));
        JsonRpcException unknown = await Assert.ThrowsAsync<JsonRpcException>(async () => await a.RequestAsync("nothing", null, TimeSpan.FromSeconds(5)));

        Assert.AreEqual("it broke", broke.Message);
        Assert.AreEqual(-32603, broke.Code);
        Assert.AreEqual(-32601, unknown.Code);
    }

    [TestMethod]
    public async Task AMessageSplitAcrossWrites_IsStillRead_AndNonAsciiTextSurvives()
    {
        var input = new MemoryStream();
        using var connection = new JsonRpcConnection(input, new MemoryStream());
        string? heard = null;
        connection.OnNotification("text", p => heard = p!.Value.GetProperty("s").GetString());
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"text\",\"params\":{\"s\":\"árvíztűrő ✓\"}}";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        byte[] all = [.. Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), .. bytes];
        connection.Start();

        input.Write(all, 0, 10);
        input.Position = 0;
        // A memory stream returns at its end, which closes the connection; feed the message whole through a pipe instead.
        (Stream writer, Stream reader) = Pipe();
        using var piped = new JsonRpcConnection(reader, new MemoryStream());
        piped.OnNotification("text", p => heard = p!.Value.GetProperty("s").GetString());
        piped.Start();
        writer.Write(all, 0, 7);
        writer.Flush();
        await Task.Delay(30);
        writer.Write(all, 7, all.Length - 7);
        writer.Flush();
        await WaitFor(() => heard is not null, "the split message");

        Assert.AreEqual("árvíztűrő ✓", heard);
    }

    [TestMethod]
    public async Task WhenTheOtherEndCloses_PendingRequestsFailAndClosedIsRaised()
    {
        (Stream aWrites, Stream bReads) = Pipe();
        (Stream bWrites, Stream aReads) = Pipe();
        using var a = new JsonRpcConnection(aReads, aWrites);
        var b = new JsonRpcConnection(bReads, bWrites);
        bool closed = false;
        a.Closed += () => closed = true;
        b.OnRequest("slow", _ => new TaskCompletionSource<object?>().Task);
        a.Start();
        b.Start();

        Task<JsonElement?> pending = a.RequestAsync("slow", null, TimeSpan.FromSeconds(5));
        await Task.Delay(30);
        b.Dispose();

        await Assert.ThrowsAsync<IOException>(async () => await pending);
        await WaitFor(() => closed, "Closed");
        Assert.IsFalse(a.IsOpen);
    }

    // ---- the client ----

    [TestMethod]
    public async Task Initialize_ReadsTheServersCapabilities_AndSaysInitialized()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            LspCapabilities caps = await client.InitializeAsync("file:///work");

            Assert.IsTrue(caps.Hover);
            Assert.IsTrue(caps.Completion);
            Assert.IsTrue(caps.Definition);
            Assert.AreEqual("fake-ls", caps.ServerName);
            Assert.IsTrue(client.IsRunning);
            await WaitFor(() => server.Received.Contains("initialized"), "initialized");
        }
    }

    [TestMethod]
    public async Task AFeatureTheServerDoesNotHave_IsReportedAsMissing()
    {
        (LspClient client, FakeServer server) = Connect(hover: false);
        using (client)
        using (server)
        {
            LspCapabilities caps = await client.InitializeAsync();

            Assert.IsFalse(caps.Hover);
            Assert.IsTrue(caps.Definition);
        }
    }

    [TestMethod]
    public async Task OpeningAndChangingADocument_PublishesDiagnostics()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            var received = new List<IReadOnlyList<LspDiagnostic>>();
            client.DiagnosticsReceived += (uri, diagnostics) =>
            {
                if (uri == Uri)
                {
                    lock (received)
                    {
                        received.Add(diagnostics);
                    }
                }
            };
            await client.InitializeAsync();

            client.OpenDocument(Uri, "csharp", "int x;\nBAD line\nTODO later");
            await WaitFor(() => received.Count >= 1, "the first diagnostics");
            LspDiagnostic[] first = [.. received[0]];

            Assert.AreEqual(2, first.Length);
            Assert.AreEqual(LspSeverity.Error, first[0].Severity);
            Assert.AreEqual(new LspRange(new LspPosition(1, 0), new LspPosition(1, 3)), first[0].Range);
            Assert.AreEqual("bad thing", first[0].Message);
            Assert.AreEqual("fake", first[0].Source);
            Assert.AreEqual("42", first[0].Code);
            Assert.AreEqual(LspSeverity.Warning, first[1].Severity);

            client.ChangeDocument(Uri, "int x;\nall good");
            await WaitFor(() => received.Count >= 2, "the diagnostics after the change");
            Assert.AreEqual(0, received[1].Count);
            Assert.AreEqual("int x;\nall good", server.Text);
        }
    }

    [TestMethod]
    public async Task ChangesCountUp_AndAnUnopenedDocumentIsNotChanged()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();

            client.ChangeDocument(Uri, "ignored");
            client.OpenDocument(Uri, "csharp", "a");
            client.ChangeDocument(Uri, "b");
            client.CloseDocument(Uri);
            client.CloseDocument(Uri);

            await WaitFor(() => server.Received.Contains("didClose"), "didClose");
            Assert.AreEqual(1, server.Received.Count(r => r == "didOpen"));
            Assert.AreEqual(1, server.Received.Count(r => r == "didChange"));
            Assert.AreEqual(1, server.Received.Count(r => r == "didClose"), "a document is closed once");
        }
    }

    [TestMethod]
    public async Task Hover_Completion_AndDefinition_AreAnsweredFromTheServer()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();
            client.OpenDocument(Uri, "csharp", "Console.WriteLine");

            string? hover = await client.HoverAsync(Uri, new LspPosition(0, 3));
            IReadOnlyList<LspCompletionItem> items = await client.CompletionAsync(Uri, new LspPosition(0, 8));
            IReadOnlyList<LspLocation> where = await client.DefinitionAsync(Uri, new LspPosition(0, 3));

            Assert.AreEqual("**int** x", hover);
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("Console", items[0].Label);
            Assert.AreEqual("class", items[0].Detail);
            Assert.AreEqual("WriteLine", items[1].InsertText, "no insertText falls back to the label");
            Assert.AreEqual(1, where.Count);
            Assert.AreEqual("file:///work/other.cs", where[0].Uri);
            Assert.AreEqual(new LspRange(new LspPosition(4, 2), new LspPosition(4, 9)), where[0].Range);
        }
    }

    [TestMethod]
    public async Task Shutdown_TellsTheServerToStopAndExit_OnlyOnce()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();

            await client.ShutdownAsync();
            await client.ShutdownAsync();

            Assert.IsTrue(server.ShutdownSeen);
            await WaitFor(() => server.ExitSeen, "exit");
            Assert.IsFalse(client.IsRunning);
        }
    }

    [TestMethod]
    public void UriOf_IsAFileUri()
    {
        string uri = LspClient.UriOf(Path.Combine(Path.GetTempPath(), "a b.cs"));

        Assert.IsTrue(uri.StartsWith("file:///", StringComparison.Ordinal), uri);
        Assert.IsTrue(uri.EndsWith("a%20b.cs", StringComparison.Ordinal), uri);
    }

    [TestMethod]
    public void AProcessThatIsNotInstalled_ReturnsNullInsteadOfThrowing()
    {
        Assert.IsNull(LspProcess.TryStart("definitely-not-a-language-server-" + Guid.NewGuid().ToString("N")));
    }

    // ---- the binding ----

    private static TextEditorControl NewEditor(params string[] lines)
    {
        Mock<IThemeService> theme = new();
        theme.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        Window root = new(theme.Object) { Position = new Position(0, 0), ViewSize = new Size(40, 8) };
        TextEditorControl editor = new(theme.Object) { Position = new Position(0, 0), ViewSize = new Size(40, 8) };
        root.AddControl(editor);
        foreach (string line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.OnInitializeLayout();
        editor.SetFocus();
        return editor;
    }

    [TestMethod]
    public async Task TheBinding_TurnsDiagnosticsIntoUnderlinesWithMessages()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();
            TextEditorControl editor = NewEditor("int x;", "  BAD here", "TODO and LONG", "  tail end");

            using var binding = new LanguageServerBinding(editor, client, Uri, "csharp", TimeSpan.FromHours(1));
            await WaitFor(() => binding.Diagnostics.Count == 3, "the diagnostics");

            TextDecoration bad = editor.Decorations.ForLine(1).Single();
            Assert.AreEqual(2, bad.Column);
            Assert.AreEqual(3, bad.Length);
            Assert.AreEqual(UnderlineStyle.Curly, bad.Underline);
            Assert.AreEqual(Color.Red, bad.UnderlineColor);
            Assert.AreEqual("fake: bad thing", bad.Message);
            Assert.AreEqual("fake: bad thing", editor.MessageAt(1, 3));
            Assert.IsNull(editor.MessageAt(1, 9));

            Assert.AreEqual(Color.Yellow, editor.Decorations.ForLine(2).First(d => d.Message == "unfinished").UnderlineColor);
            // The range that starts on line 2 and ends on line 3 marks both lines.
            Assert.IsTrue(editor.Decorations.ForLine(2).Any(d => d.Message == "spans lines"));
            TextDecoration tail = editor.Decorations.ForLine(3).Single(d => d.Message == "spans lines");
            Assert.AreEqual(0, tail.Column);
            Assert.AreEqual(3, tail.Length);
        }
    }

    [TestMethod]
    public async Task TheBinding_SendsChangesOnceAndRefreshesTheMarks()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();
            TextEditorControl editor = NewEditor("BAD");
            using var binding = new LanguageServerBinding(editor, client, Uri, "csharp", TimeSpan.FromHours(1));
            await WaitFor(() => binding.Diagnostics.Count == 1, "the first diagnostics");

            Assert.IsFalse(binding.Flush(), "nothing changed yet");
            editor.TextModel.Lines[0] = "fine";
            Assert.IsTrue(binding.Flush());
            Assert.IsFalse(binding.Flush(), "sent once");

            await WaitFor(() => editor.Decorations.Count == 0, "the marks to go");
            Assert.AreEqual(0, binding.Diagnostics.Count);
            Assert.AreEqual("fine", server.Text);
        }
    }

    [TestMethod]
    public async Task TheBinding_AsksHoverCompletionAndDefinitionAtTheCursor_AndFlushesFirst()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();
            TextEditorControl editor = NewEditor("Console.Write");
            using var binding = new LanguageServerBinding(editor, client, Uri, "csharp", TimeSpan.FromHours(1));
            editor.TextModel.Lines[0] = "Console.WriteLine";
            editor.ScrollableModel.CursorPosition.X = 8;

            string? hover = await binding.HoverAtCursorAsync();
            IReadOnlyList<LspCompletionItem> items = await binding.CompletionAtCursorAsync();
            IReadOnlyList<LspLocation> where = await binding.DefinitionAtCursorAsync();

            Assert.AreEqual("**int** x", hover);
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(1, where.Count);
            Assert.AreEqual("Console.WriteLine", server.Text, "the server was told the new text before it was asked");
        }
    }

    [TestMethod]
    public async Task TheBinding_OnlyListensToItsOwnDocument_AndDisposeClosesItAndRemovesTheMarks()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();
            TextEditorControl editor = NewEditor("BAD");
            var binding = new LanguageServerBinding(editor, client, "file:///work/other.cs", "csharp", TimeSpan.FromHours(1));
            await WaitFor(() => server.Received.Contains("didOpen"), "didOpen");
            await Task.Delay(100);
            Assert.AreEqual(0, editor.Decorations.Count, "diagnostics for another uri are not ours");

            using var own = new LanguageServerBinding(NewEditor("BAD"), client, Uri, "csharp", TimeSpan.FromHours(1));
            await WaitFor(() => own.Diagnostics.Count == 1, "our diagnostics");

            binding.Dispose();
            binding.Dispose();
            await WaitFor(() => server.Received.Contains("didClose"), "didClose");
        }
    }

    [TestMethod]
    public async Task NextDiagnostic_GoesDownTheFileAndWrapsRound()
    {
        (LspClient client, FakeServer server) = Connect();
        using (client)
        using (server)
        {
            await client.InitializeAsync();
            TextEditorControl editor = NewEditor("a", "BAD", "b", "TODO");
            using var binding = new LanguageServerBinding(editor, client, Uri, "csharp", TimeSpan.FromHours(1));
            await WaitFor(() => binding.Diagnostics.Count == 2, "the diagnostics");

            Assert.AreEqual(1, binding.NextDiagnostic(0)!.Range.Start.Line);
            Assert.AreEqual(3, binding.NextDiagnostic(1)!.Range.Start.Line);
            Assert.AreEqual(1, binding.NextDiagnostic(3)!.Range.Start.Line, "wraps to the first");
        }
    }
}
