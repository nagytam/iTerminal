using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace iTerminal.Core.AI;

/// <summary>
/// Builds the one chat client every AI feature uses. Each provider is an OpenAI-compatible endpoint reached through
/// <c>Microsoft.Extensions.AI</c>'s OpenAI adapter: Ollama serves <c>/v1</c> itself, so it needs no client of its own.
/// </summary>
public static class AiChatClientFactory
{
    // Ollama ignores the key, but the OpenAI client refuses to send a request without one.
    private const string NoKeyPlaceholder = "no-api-key-configured";

    /// <param name="options">The configured provider, endpoint and model.</param>
    /// <param name="apiKey">The key for the endpoint, or null when none is configured.</param>
    /// <param name="httpClient">Only for tests: the transport the requests are sent through.</param>
    public static IChatClient Create(AiOptions options, string? apiKey, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Provider == AiProvider.None)
        {
            return new UnavailableChatClient();
        }

        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(options.ApiBaseUrl) };
        if (httpClient is not null)
        {
            clientOptions.Transport = new HttpClientPipelineTransport(httpClient);
        }

        bool isOllama = options.Provider == AiProvider.Ollama;
        if (isOllama)
        {
            // A local model that is still loading answers 500 at once; retrying it three times with back-off only delays
            // the answer, and the fallback below needs the first 500 to come back directly.
            clientOptions.RetryPolicy = new ClientRetryPolicy(maxRetries: 0);
        }

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? NoKeyPlaceholder : apiKey),
            clientOptions);

        IChatClient inner = openAiClient.GetChatClient(options.ModelId).AsIChatClient();
        if (isOllama)
        {
            inner = new ToolFallbackChatClient(inner);
        }

        var builder = inner.AsBuilder();
        if (isOllama && options.DisableThinking)
        {
            builder.ConfigureOptions(chatOptions => chatOptions.Reasoning ??= new ReasoningOptions { Effort = ReasoningEffort.None });
        }

        return builder.UseFunctionInvocation().Build();
    }

    /// <summary>
    /// Some Ollama model templates (qwen3.5 at the time of writing) fail to render tool definitions and answer 500.
    /// Such a request is sent again without tools, so the assistant still answers, just without tool calling.
    /// </summary>
    internal sealed class ToolFallbackChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (ClientResultException exception) when (exception.Status == 500 && options?.Tools is { Count: > 0 })
            {
                ChatOptions withoutTools = options.Clone();
                withoutTools.Tools = null;
                withoutTools.ToolMode = null;
                return await base.GetResponseAsync(messages, withoutTools, cancellationToken);
            }
        }
    }

    /// <summary>The client registered when AI is off. Nothing reaches it: the availability check says so first.</summary>
    internal sealed class UnavailableChatClient : IChatClient
    {
        private static string Message => iConsole.Localization.UiText.Get("AI.Off", "AI is off (AI:Provider is None).");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
