using System.Net;
using System.Text;
using System.Text.Json;
using iTerminal.Core.AI;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.AI;

/// <summary>
/// The one chat client every AI feature uses, checked against the request bodies it sends. The server is a scripted
/// handler, so nothing needs Ollama or a key: what is pinned down is what leaves the process.
/// </summary>
[TestClass]
public class AiChatClientFactoryTest
{
    private const string BaseUrl = "http://localhost:11434/v1/";
    private const string Answer = """{"id":"1","object":"chat.completion","created":0,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}]}""";

    [TestMethod]
    public async Task Ollama_WithDisableThinking_AsksForNoReasoning()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, Answer));
        var client = AiChatClientFactory.Create(new AiOptions { Provider = AiProvider.Ollama, ApiBaseUrl = BaseUrl, DisableThinking = true }, null, new HttpClient(handler));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.AreEqual("none", Root(handler.Bodies.Single()).GetProperty("reasoning_effort").GetString());
        Assert.AreEqual(new Uri(BaseUrl + "chat/completions"), handler.Uris.Single());
    }

    [TestMethod]
    public async Task Ollama_WithThinkingAllowed_SendsNoReasoningField()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, Answer));
        var client = AiChatClientFactory.Create(new AiOptions { Provider = AiProvider.Ollama, ApiBaseUrl = BaseUrl, DisableThinking = false }, null, new HttpClient(handler));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.IsFalse(Root(handler.Bodies.Single()).TryGetProperty("reasoning_effort", out _));
    }

    [TestMethod]
    public async Task OpenAI_NeverSendsTheOllamaOnlyReasoningField()
    {
        // Models behind a hosted API may refuse reasoning_effort, so DisableThinking is an Ollama setting only.
        var handler = new ScriptedHandler((HttpStatusCode.OK, Answer));
        var client = AiChatClientFactory.Create(new AiOptions { Provider = AiProvider.OpenAI, ApiBaseUrl = "https://example.test/v1/", DisableThinking = true }, "key", new HttpClient(handler));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.IsFalse(Root(handler.Bodies.Single()).TryGetProperty("reasoning_effort", out _));
        Assert.AreEqual("Bearer key", handler.Authorizations.Single());
    }

    [TestMethod]
    public async Task Ollama_WhenTheServerRefusesTools_RetriesWithoutThem()
    {
        var tool = AIFunctionFactory.Create(() => "C:/", "get_current_path", "Get the path");
        var handler = new ScriptedHandler((HttpStatusCode.InternalServerError, "{}"), (HttpStatusCode.OK, Answer));
        var client = AiChatClientFactory.Create(new AiOptions { Provider = AiProvider.Ollama, ApiBaseUrl = BaseUrl }, null, new HttpClient(handler));

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions { Tools = [tool] });

        Assert.AreEqual(2, handler.Bodies.Count);
        Assert.IsTrue(Root(handler.Bodies[0]).TryGetProperty("tools", out _), "The first request should carry the tools.");
        Assert.IsFalse(Root(handler.Bodies[1]).TryGetProperty("tools", out _), "The retry should carry none.");
        Assert.AreEqual("hello", response.Text);
    }

    [TestMethod]
    public async Task OpenAI_ServerErrorWithTools_IsReportedNotRetried()
    {
        var tool = AIFunctionFactory.Create(() => "C:/", "get_current_path", "Get the path");
        var handler = new ScriptedHandler((HttpStatusCode.InternalServerError, "{}"));
        var client = AiChatClientFactory.Create(new AiOptions { Provider = AiProvider.OpenAI, ApiBaseUrl = "https://example.test/v1/" }, "key", new HttpClient(handler));

        await Assert.ThrowsExactlyAsync<System.ClientModel.ClientResultException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions { Tools = [tool] }));
        Assert.IsTrue(handler.Bodies.All(body => Root(body).TryGetProperty("tools", out _)),
            "Every attempt keeps the tools: the fallback that drops them is for Ollama alone.");
    }

    [TestMethod]
    public async Task None_SendsNothingAndSaysAIIsOff()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, Answer));
        var options = new AiOptions { Provider = AiProvider.None };
        var chat = new AiChatService(AiChatClientFactory.Create(options, null, new HttpClient(handler)), Mock.Of<IContentModelService>(), Mock.Of<IUiService>(), Options.Create(options));
        var model = new AiChatModel();

        await chat.CheckAvailabilityAsync(model);

        Assert.IsFalse(model.IsAvailable);
        Assert.AreEqual("AI is off (AI:Provider)", model.ModelInfo);
        Assert.AreEqual(0, handler.Bodies.Count, "With AI off no request may leave the process.");
        model.Input = "hello";
        await chat.SendMessageAsync(model);
        StringAssert.StartsWith(model.LastResponse, "Error:", "A send that gets past the check is reported, not sent.");
        Assert.AreEqual(0, handler.Bodies.Count);
    }

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    /// <summary>Answers each request in turn with the last script entry repeated, and records what was asked.</summary>
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] script) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _script = new(script);
        private readonly object _gate = new();

        public List<string> Bodies { get; } = [];
        public List<Uri> Uris { get; } = [];
        public List<string> Authorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            (HttpStatusCode status, string answer) = _script.Count > 1 ? _script.Dequeue() : _script.Peek();
            lock (_gate)
            {
                Bodies.Add(body);
                Uris.Add(request.RequestUri!);
                Authorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
