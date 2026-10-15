using iTerminal.Core.Test.Helper;
using System.Net;
using System.Text;
using iTerminal.Core.AI;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class OllamaServerTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToList();
                }
            }
        }

        public void Report(string value)
        {
            lock (_lines)
            {
                _lines.Add(value);
            }
        }
    }

    private static OllamaServer NewServer(Func<HttpRequestMessage, HttpResponseMessage> respond, string address = "http://localhost:11434/v1/") =>
        new(new Uri(address), new HttpClient(new StubHandler(respond)));

    private static HttpResponseMessage Ndjson(params string[] lines) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Join("\n", lines) + "\n", Encoding.UTF8, "application/x-ndjson"),
    };

    [TestMethod]
    public async Task IsReachable_IsTrueWhenTheVersionAnswers()
    {
        OllamaServer server = NewServer(request => request.RequestUri!.AbsolutePath == "/api/version"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.IsTrue(await server.IsReachableAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task IsReachable_IsFalseWhenNothingListens()
    {
        OllamaServer server = NewServer(_ => throw new HttpRequestException("connection refused"));

        Assert.IsFalse(await server.IsReachableAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task EnsureRunning_DoesNotStartAServerThatAlreadyAnswers()
    {
        OllamaServer server = NewServer(_ => new HttpResponseMessage(HttpStatusCode.OK));

        bool started = await server.EnsureRunningAsync("/does/not/exist/ollama", "/tmp/models", CancellationToken.None);

        Assert.IsFalse(started, "A server that is already there is used, not replaced.");
    }

    [TestMethod]
    public async Task EnsureRunning_RefusesToStartAServerAtAnotherComputer()
    {
        OllamaServer server = NewServer(_ => throw new HttpRequestException("connection refused"), "http://ollama.example.net:11434/v1/");

        await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            server.EnsureRunningAsync("/does/not/exist/ollama", "/tmp/models", CancellationToken.None));
    }

    [TestMethod]
    public void IsLocal_OnlyForThisComputer()
    {
        Assert.IsTrue(NewServer(_ => throw new InvalidOperationException()).IsLocal);
        Assert.IsFalse(NewServer(_ => throw new InvalidOperationException(), "http://ollama.example.net:11434/").IsLocal);
    }

    [TestMethod]
    public async Task Pull_ReportsProgressAndEndsAtSuccess()
    {
        OllamaServer server = NewServer(request =>
        {
            Assert.AreEqual("/api/pull", request.RequestUri!.AbsolutePath);
            return Ndjson(
                "{\"status\":\"pulling manifest\"}",
                "{\"status\":\"downloading\",\"total\":2000000,\"completed\":1000000}",
                "{\"status\":\"success\"}");
        });
        var progress = new RecordingProgress();

        await server.PullAsync("qwen3.5:0.8b", progress, CancellationToken.None);

        CollectionAssert.Contains(progress.Lines.ToList(), "pulling manifest");
        Assert.AreEqual("success", progress.Lines[^1]);
    }

    [TestMethod]
    public async Task Pull_RaisesTheErrorOllamaReports()
    {
        OllamaServer server = NewServer(_ => Ndjson("{\"error\":\"pull model manifest: file does not exist\"}"));

        InvalidOperationException error = await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            server.PullAsync("nope:1b", null, CancellationToken.None));

        StringAssert.Contains(error.Message, "file does not exist");
    }

    [TestMethod]
    public async Task Pull_FailsWhenTheDownloadEndsWithoutSuccess()
    {
        OllamaServer server = NewServer(_ => Ndjson("{\"status\":\"downloading\"}"));

        await AssertAsync.ThrowsAsync<InvalidOperationException>(() =>
            server.PullAsync("qwen3.5:0.8b", null, CancellationToken.None));
    }
}
