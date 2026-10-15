using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using System.Text.Json;

namespace iTerminal.Core.Test.AI;

[TestClass]
[Ignore("Requires a running Ollama instance — run manually with: dotnet test --filter OllamaAvailability")]
public class OllamaAvailabilityTest
{
    private const string OllamaBaseUrl = "http://localhost:11434";

    [TestMethod]
    [Timeout(5000)]
    public async Task Ollama_ServerIsReachable()
    {
        using var client = new HttpClient();

        var response = await client.GetAsync($"{OllamaBaseUrl}/api/tags");

        Assert.IsTrue(response.IsSuccessStatusCode,
            $"Ollama server at {OllamaBaseUrl} returned {(int)response.StatusCode}");
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task Ollama_ConfiguredModelIsAvailable()
    {
        const string expectedModel = "qwen3.5:latest";
        using var client = new HttpClient();

        var response = await client.GetAsync($"{OllamaBaseUrl}/api/tags");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var models = doc.RootElement.GetProperty("models");

        var found = models.EnumerateArray()
            .Any(m => m.GetProperty("name").GetString() == expectedModel);

        Assert.IsTrue(found, $"Model '{expectedModel}' is not available. Pull it with: ollama pull {expectedModel}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task Ollama_ModelRespondsToChat()
    {
        const string expectedModel = "qwen3.5:latest";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(110) };

        // /no_think disables chain-of-thought tokens so the response is faster
        var requestBody = JsonSerializer.Serialize(new
        {
            model = expectedModel,
            messages = new[] { new { role = "user", content = "/no_think Reply with the single word: ok" } },
            stream = false
        });

        var response = await client.PostAsync(
            $"{OllamaBaseUrl}/v1/chat/completions",
            new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.IsTrue(response.IsSuccessStatusCode,
            $"Chat request failed with {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(content), "Model returned empty response");
    }
}
