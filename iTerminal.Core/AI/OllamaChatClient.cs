using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace iTerminal.Core.AI;

/// <summary>
/// Minimal <see cref="IChatClient"/> that talks to Ollama's native <c>/api/chat</c> endpoint.
/// Unlike Ollama's OpenAI-compatible <c>/v1/chat/completions</c> endpoint, the native API honours
/// the <c>think</c> flag, which lets us disable the slow chain-of-thought generation of reasoning
/// models (e.g. qwen3.5) while still supporting tool/function calling. Only the non-streaming
/// path is implemented because <c>AiChatService</c> uses <see cref="GetResponseAsync"/>; the
/// streaming method adapts that single response.
/// </summary>
public sealed class OllamaChatClient : IChatClient
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _model;
    private readonly bool _think;
    private readonly Uri _chatEndpoint;
    // Ollama doesn't return tool-call ids; we synthesize them and remember the tool name so a
    // follow-up tool-result message can be tagged back with the originating tool name.
    private readonly ConcurrentDictionary<string, string> _callIdToToolName = new();

    public OllamaChatClient(Uri apiBaseUrl, string model, bool think, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _think = think;
        // ApiBaseUrl is typically the OpenAI-compatible ".../v1/"; the native API lives at "/api/chat"
        // on the same host, so resolve against the authority.
        _chatEndpoint = new Uri(apiBaseUrl, "/api/chat");
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        if (_http.Timeout < TimeSpan.FromMinutes(5))
        {
            _http.Timeout = TimeSpan.FromMinutes(5);
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var mappedMessages = MapMessages(messages);
        var tools = MapTools(options?.Tools);

        var httpResponse = await PostChatAsync(mappedMessages, tools, cancellationToken);
        try
        {
            // Some model templates in current Ollama builds (e.g. qwen3.5) fail to render tool
            // definitions and return 500. Degrade gracefully to a plain chat request so the
            // assistant still answers, just without tool calling, instead of surfacing an error.
            if (tools is not null
                && httpResponse.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            {
                httpResponse.Dispose();
                httpResponse = await PostChatAsync(mappedMessages, tools: null, cancellationToken);
            }

            httpResponse.EnsureSuccessStatusCode();
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body);
        }
        finally
        {
            httpResponse.Dispose();
        }
    }

    private Task<HttpResponseMessage> PostChatAsync(
        List<Dictionary<string, object?>> mappedMessages,
        List<object>? tools,
        CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = mappedMessages,
            ["stream"] = false,
            ["think"] = _think,
        };

        if (tools is not null)
        {
            request["tools"] = tools;
        }

        return _http.PostAsJsonAsync(_chatEndpoint, request, cancellationToken);
    }

    private ChatResponse ParseResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);

        var contents = new List<AIContent>();
        if (doc.RootElement.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    contents.Add(new TextContent(text));
                }
            }

            if (message.TryGetProperty("tool_calls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    if (!toolCall.TryGetProperty("function", out var function)
                        || !function.TryGetProperty("name", out var nameElement))
                    {
                        continue;
                    }

                    var name = nameElement.GetString() ?? string.Empty;
                    var arguments = new Dictionary<string, object?>();
                    if (function.TryGetProperty("arguments", out var args)
                        && args.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in args.EnumerateObject())
                        {
                            arguments[property.Name] = property.Value.Clone();
                        }
                    }

                    var callId = Guid.NewGuid().ToString("N");
                    _callIdToToolName[callId] = name;
                    contents.Add(new FunctionCallContent(callId, name, arguments));
                }
            }
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private List<Dictionary<string, object?>> MapMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var message in messages)
        {
            // Tool results must be emitted as standalone {role:"tool"} messages.
            var results = message.Contents.OfType<FunctionResultContent>().ToList();
            if (results.Count > 0)
            {
                foreach (var functionResult in results)
                {
                    var toolMessage = new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["content"] = ResultToString(functionResult.Result),
                    };
                    if (_callIdToToolName.TryGetValue(functionResult.CallId, out var toolName))
                    {
                        toolMessage["tool_name"] = toolName;
                    }
                    result.Add(toolMessage);
                }
                continue;
            }

            var mapped = new Dictionary<string, object?>
            {
                ["role"] = MapRole(message.Role),
                ["content"] = message.Text ?? string.Empty,
            };

            var calls = message.Contents.OfType<FunctionCallContent>().ToList();
            if (calls.Count > 0)
            {
                mapped["tool_calls"] = calls.Select(call => new
                {
                    function = new
                    {
                        name = call.Name,
                        arguments = call.Arguments ?? new Dictionary<string, object?>(),
                    },
                }).ToList();
            }

            result.Add(mapped);
        }

        return result;
    }

    private static string MapRole(ChatRole role) =>
        role == ChatRole.System ? "system"
        : role == ChatRole.Assistant ? "assistant"
        : role == ChatRole.Tool ? "tool"
        : "user";

    private static string ResultToString(object? result) =>
        result switch
        {
            null => string.Empty,
            string s => s,
            _ => JsonSerializer.Serialize(result),
        };

    private static List<object>? MapTools(IList<AITool>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        var mapped = new List<object>();
        foreach (var tool in tools.OfType<AIFunction>())
        {
            mapped.Add(new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.JsonSchema,
                },
            });
        }

        return mapped.Count > 0 ? mapped : null;
    }
}
