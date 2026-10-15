using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.AI;
using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace iTerminal.Core.Test.AI;

[TestClass]
[Ignore("Requires a running Ollama instance with qwen3.5:0.8b and llama3.2 — run manually with: dotnet test --filter OllamaChatClient")]
public class OllamaChatClientTest
{
    private const string BaseUrl = "http://localhost:11434/v1/";
    private const string ThinkingModel = "qwen3.5:0.8b";   // reasoning model; tools unsupported in current Ollama
    private const string ToolModel = "llama3.2:latest";    // supports tool calling

    [TestMethod]
    [Timeout(120000)]
    public async Task ThinkDisabled_ReturnsNonEmptyContentQuickly()
    {
        using var client = new OllamaChatClient(new Uri(BaseUrl), ThinkingModel, think: false);

        var sw = Stopwatch.StartNew();
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply in one short sentence: are you working?")]);
        sw.Stop();

        var text = response.Text;
        Assert.IsFalse(string.IsNullOrWhiteSpace(text), "Model returned empty content with think disabled.");
        Assert.IsFalse(text.Contains("<think>"), "Reasoning block leaked into the response.");
        Console.WriteLine($"[{sw.ElapsedMilliseconds} ms] {text}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task ToolCalling_InvokesRegisteredFunction()
    {
        var toolWasCalled = false;
        var tool = AIFunctionFactory.Create(
            () => { toolWasCalled = true; return "C:/example/path"; },
            "get_current_path",
            "Get the path currently shown in the focused file panel");

        using var inner = new OllamaChatClient(new Uri(BaseUrl), ToolModel, think: false);
        var client = inner.AsBuilder().UseFunctionInvocation().Build();

        var options = new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.Auto };
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Use your tools to tell me the current folder path.")],
            options);

        Assert.IsTrue(toolWasCalled, "The registered tool was not invoked by the model.");
        Console.WriteLine(response.Text);
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task ToolsRequested_OnModelWithBrokenToolTemplate_FallsBackToPlainChat()
    {
        // qwen3.5's tool template currently fails server-side (HTTP 500). The client must degrade
        // to a plain chat request so the AI bar still gets an answer instead of an error.
        var tool = AIFunctionFactory.Create(
            () => "C:/example/path",
            "get_current_path",
            "Get the path currently shown in the focused file panel");

        using var client = new OllamaChatClient(new Uri(BaseUrl), ThinkingModel, think: false);
        var options = new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.Auto };

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply in one short sentence: hello")],
            options);

        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Text),
            "Fallback to plain chat should still return content for a model with a broken tool template.");
        Console.WriteLine(response.Text);
    }
}
