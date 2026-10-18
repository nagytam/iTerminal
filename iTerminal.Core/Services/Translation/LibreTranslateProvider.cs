using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace iTerminal.Core.Services.Translation;

/// <summary>
/// The translation API: any server that speaks LibreTranslate's JSON (<c>POST /translate</c>, <c>POST /detect</c>) —
/// a self-hosted LibreTranslate, or one of its hosted services with a key. The key is read per request, so
/// unlocking the password manager or setting the variable takes effect without a restart.
/// </summary>
public sealed class LibreTranslateProvider : ITranslationProvider
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly Func<string?> _apiKey;

    public LibreTranslateProvider(string apiUrl, Func<string?> apiKey, HttpClient? client = null)
    {
        _baseUrl = (apiUrl ?? string.Empty).Trim().TrimEnd('/');
        _apiKey = apiKey;
        _client = client ?? SharedClient;
    }

    public string Id => "api";

    public string Name => "Translation API";

    // LibreTranslate refuses requests over its character limit (5000 by default).
    public int MaxChunkLength => 4000;

    public string? UnavailableReason =>
        _baseUrl.Length == 0 ? "no translation API set up (Translation:ApiUrl)" :
        Uri.TryCreate(_baseUrl, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? null :
        "the translation API URL is not an http(s) address (Translation:ApiUrl)";

    public async Task<TranslationResult> TranslateAsync(string text, string? sourceCode, string targetCode, CancellationToken cancellationToken = default)
    {
        JsonNode response = await PostAsync("/translate", new JsonObject
        {
            ["q"] = text,
            ["source"] = string.IsNullOrEmpty(sourceCode) ? TranslationLanguages.Auto : sourceCode,
            ["target"] = targetCode,
            ["format"] = "text",
        }, cancellationToken);

        string? translated = response["translatedText"]?.GetValue<string>();
        if (translated is null)
        {
            throw new TranslationException($"{Name} sent no translation.");
        }

        string? detected = response["detectedLanguage"]?["language"]?.GetValue<string>();
        return new TranslationResult(translated, TranslationLanguages.Find(detected)?.Code ?? detected, Name);
    }

    public async Task<LanguageDetection?> DetectAsync(string text, CancellationToken cancellationToken = default)
    {
        JsonNode response = await PostAsync("/detect", new JsonObject { ["q"] = text }, cancellationToken);
        if (response is not JsonArray { Count: > 0 } hits)
        {
            return null;
        }

        JsonNode best = hits.OrderByDescending(hit => hit?["confidence"]?.GetValue<double>() ?? 0).First()!;
        string? code = best["language"]?.GetValue<string>();
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        // LibreTranslate reports confidence from 0 to 100.
        double confidence = (best["confidence"]?.GetValue<double>() ?? 0) / 100;
        return new LanguageDetection(TranslationLanguages.Find(code)?.Code ?? code, Math.Clamp(confidence, 0, 1));
    }

    private async Task<JsonNode> PostAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        if (UnavailableReason is { } reason)
        {
            throw new TranslationException($"{Name}: {reason}.");
        }

        string? key = _apiKey();
        if (!string.IsNullOrEmpty(key))
        {
            body["api_key"] = key;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException($"{Name}: {Describe(response.StatusCode, content, !string.IsNullOrEmpty(key))}");
            }

            return JsonNode.Parse(content) ?? throw new TranslationException($"{Name} sent an empty answer.");
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException($"{Name} is unreachable: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException($"{Name} timed out.", exception);
        }
        catch (JsonException exception)
        {
            throw new TranslationException($"{Name} sent an answer that is not JSON.", exception);
        }
    }

    /// <summary>Says what to do about a refused request: the server's own message when it sent one, else what the status code usually means.</summary>
    internal static string Describe(HttpStatusCode status, string body, bool keySet)
    {
        string? message = null;
        try
        {
            message = JsonNode.Parse(body)?["error"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // Not the JSON error the API normally sends; the status code has to do.
        }

        string meaning = status switch
        {
            HttpStatusCode.BadRequest => "the request was refused",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when !keySet => "an API key is needed — set LIBRETRANSLATE_API_KEY",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "the API key was refused",
            HttpStatusCode.NotFound => "no such endpoint — check Translation:ApiUrl",
            HttpStatusCode.TooManyRequests => "rate limited, try again later",
            >= HttpStatusCode.InternalServerError => "server error",
            _ => "the request failed",
        };

        return string.IsNullOrWhiteSpace(message) ? $"{meaning} ({(int)status})." : $"{message} ({(int)status}, {meaning}).";
    }
}
