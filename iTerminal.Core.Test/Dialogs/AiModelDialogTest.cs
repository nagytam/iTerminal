using System.Net;
using System.Net.Sockets;
using System.Text;
using iTerminal.Core.AI;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.Options;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The AI model information dialog. The model details come from the model server over HTTP; these
/// tests stand up a tiny local server (or deliberately none) so the three outcomes — slow, down and
/// answering — are all deterministic.
/// </summary>
[TestClass]
public class AiModelDialogTest
{
    /// <summary>A one-shot HTTP responder on a free loopback port.</summary>
    private sealed class LocalServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();

        public LocalServer(string? body)
        {
            _listener.Start();
            _ = Task.Run(() => ServeAsync(body));
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1/";

        private async Task ServeAsync(string? body)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer, _cts.Token);

                if (body is null)
                {
                    // Accept and never answer: the dialog's request hangs until its own timeout.
                    await Task.Delay(Timeout.Infinite, _cts.Token);
                    return;
                }

                var payload = Encoding.UTF8.GetBytes(body);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK" + "\r\n" +
                    "Content-Type: application/json" + "\r\n" +
                    $"Content-Length: {payload.Length}" + "\r\n" +
                    "Connection: close" + "\r\n\r\n");
                await stream.WriteAsync(header, _cts.Token);
                await stream.WriteAsync(payload, _cts.Token);
            }
            catch
            {
                // Cancelled at dispose.
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }

    private static AiModelDialog Create(string baseUrl, AiChatModel? model = null) =>
        new(model ?? new AiChatModel(),
            Options.Create(new AiOptions { ApiBaseUrl = baseUrl, ModelId = "qwen3", ApiKeyEnvVar = "ITERMINAL_TEST_NO_SUCH_KEY" }),
            TestTheme());

    private static string ClosedPortUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"http://127.0.0.1:{port}/v1/";
    }

    [TestMethod]
    public async Task OpensAtOnceWithoutWaitingForTheModelServer()
    {
        // The details request used to be awaited before the dialog could appear — up to its full
        // five-second timeout with nothing on screen when the server was slow or down.
        using var server = new LocalServer(body: null);
        var dialog = Create(server.BaseUrl);

        var started = DateTime.UtcNow;
        await ShowAsync(dialog);

        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "the dialog must not wait for HTTP");
        CollectionAssert.Contains(dialog.LinesForTest.ToList(), "  Loading from the model server…");
    }

    [TestMethod]
    public async Task AnUnreachableServerIsSaidRatherThanLeftOut()
    {
        var dialog = Create(ClosedPortUrl());
        await ShowAsync(dialog);

        await dialog.DetailsTask;

        Assert.IsTrue(dialog.LinesForTest.Any(l => l.Contains("Not available")), string.Join(Environment.NewLine, dialog.LinesForTest));
        Assert.IsFalse(dialog.LinesForTest.Any(l => l.Contains("Loading")), "the loading line is replaced");
    }

    [TestMethod]
    public async Task TheServersModelDetailsAreShown()
    {
        using var server = new LocalServer("""
            {"data":[{"id":"qwen3","size":2147483648,"details":{"family":"qwen","parameter_size":"8B","quantization_level":"Q4_K_M","format":"gguf"}}]}
            """);
        var dialog = Create(server.BaseUrl);
        await ShowAsync(dialog);

        await dialog.DetailsTask;

        var lines = dialog.LinesForTest;
        CollectionAssert.Contains(lines.ToList(), "  Parameters     : 8B");
        CollectionAssert.Contains(lines.ToList(), "  Family         : qwen");
        CollectionAssert.Contains(lines.ToList(), "  Quantization   : Q4_K_M");
        CollectionAssert.Contains(lines.ToList(), "  Size           : 2.00 GB");
    }

    [TestMethod]
    public async Task StatusConfigurationAndSessionAreShown()
    {
        var model = new AiChatModel { IsAvailable = true, ModelInfo = "qwen3 ready" };
        model.History.Add(new AiChatMessage("user", "hi"));
        model.History.Add(new AiChatMessage("assistant", "hello"));
        var dialog = Create(ClosedPortUrl(), model);

        await ShowAsync(dialog);

        var lines = dialog.LinesForTest.ToList();
        CollectionAssert.Contains(lines, "  Available      : Yes");
        CollectionAssert.Contains(lines, "  Info           : qwen3 ready");
        CollectionAssert.Contains(lines, "  Model ID       : qwen3");
        CollectionAssert.Contains(lines, "  API Key Set    : No (unauthenticated)");
        CollectionAssert.Contains(lines, "  Messages       : 2 (1 user, 1 assistant)");
        CollectionAssert.Contains(lines, "  Esc:close");
        await dialog.DetailsTask;
    }

    [TestMethod]
    public async Task EscapeClosesTheDialog()
    {
        var dialog = Create(ClosedPortUrl());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
        await dialog.DetailsTask;
    }
}
