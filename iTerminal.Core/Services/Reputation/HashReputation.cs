using iConsole.Localization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace iTerminal.Core.Services.Reputation;

/// <summary>How a reputation look-up ended.</summary>
public enum ReputationStatus
{
    Found,
    NotFound,
    RateLimited,
    Offline,
    InvalidKey,
    Error,
}

/// <summary>What a reputation service knows about one file hash.</summary>
public sealed record ReputationResult(
    ReputationStatus Status,
    int Malicious = 0,
    int Suspicious = 0,
    int Total = 0,
    DateTimeOffset? FirstSeen = null,
    DateTimeOffset? LastSeen = null,
    string? Link = null,
    TimeSpan? RetryAfter = null,
    string? Message = null)
{
    /// <summary>Engines that flagged the file: malicious plus suspicious.</summary>
    public int Detections => Malicious + Suspicious;
}

/// <summary>Asks a reputation service about a file's hash. Only the hash is ever sent, never the file.</summary>
public interface IHashReputationService
{
    /// <summary>Looks <paramref name="sha256"/> up with <paramref name="apiKey"/>. Never throws for a network or server failure; those are statuses.</summary>
    Task<ReputationResult> LookUpAsync(string sha256, string apiKey, CancellationToken cancellationToken = default);
}

/// <summary>Finds the VirusTotal API key in the password manager.</summary>
public static class VirusTotalKey
{
    /// <summary>The title an entry must start with (case-insensitive); its password is the API key.</summary>
    public const string TitlePrefix = "VirusTotal";

    /// <summary>The first matching entry's password, or null when the manager is locked, missing or has none.</summary>
    public static string? Resolve(IPasswordManager? passwords)
    {
        if (passwords is not { IsUnlocked: true })
        {
            return null;
        }

        var entry = passwords.List().FirstOrDefault(e =>
            e.Title.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase) && e.Password.Trim().Length > 0);
        return entry?.Password.Trim();
    }
}

/// <summary>
/// VirusTotal v3: <c>GET /api/v3/files/{sha256}</c> with the key in the <c>x-apikey</c> header. The key
/// is never part of the address and nothing is logged.
/// </summary>
public sealed class VirusTotalReputationService : IHashReputationService
{
    private const string ApiBase = "https://www.virustotal.com/api/v3/files/";
    private const string GuiBase = "https://www.virustotal.com/gui/file/";

    private readonly HttpClient _http;

    public VirusTotalReputationService(HttpClient http) => _http = http;

    /// <summary>The page a person opens in the browser for <paramref name="sha256"/>.</summary>
    public static string GuiLink(string sha256) => GuiBase + sha256.ToLowerInvariant();

    public async Task<ReputationResult> LookUpAsync(string sha256, string apiKey, CancellationToken cancellationToken = default)
    {
        var hash = sha256.Trim().ToLowerInvariant();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            return new ReputationResult(ReputationStatus.Error, Message: UiText.Get("Rep.NotSha", "Not a SHA-256."));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + hash);
            request.Headers.Add("x-apikey", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return Parse(body, hash);
                case HttpStatusCode.NotFound:
                    return new ReputationResult(ReputationStatus.NotFound, Link: GuiLink(hash));
                case HttpStatusCode.TooManyRequests:
                    var wait = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                    return new ReputationResult(ReputationStatus.RateLimited, RetryAfter: wait is { } w && w > TimeSpan.Zero ? w : null);
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return new ReputationResult(ReputationStatus.InvalidKey);
                default:
                    return new ReputationResult(ReputationStatus.Error, Message: $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (HttpRequestException exception)
        {
            return new ReputationResult(ReputationStatus.Offline, Message: exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout: as good as offline.
            return new ReputationResult(ReputationStatus.Offline, Message: UiText.Get("Rep.Timeout", "The request timed out."));
        }
    }

    /// <summary>Reads a 200 answer. Anything that is not the expected shape is an <see cref="ReputationStatus.Error"/>.</summary>
    public static ReputationResult Parse(string json, string sha256)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
            var stats = attributes.GetProperty("last_analysis_stats");
            int Count(string name) => stats.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : 0;
            var malicious = Count("malicious");
            var suspicious = Count("suspicious");
            var total = malicious + suspicious + Count("undetected") + Count("harmless");

            DateTimeOffset? Date(string name) =>
                attributes.TryGetProperty(name, out var value) && value.TryGetInt64(out var seconds) && seconds > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null;

            return new ReputationResult(
                ReputationStatus.Found,
                malicious,
                suspicious,
                total,
                Date("first_submission_date"),
                Date("last_submission_date") ?? Date("last_analysis_date"),
                GuiLink(sha256));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new ReputationResult(ReputationStatus.Error, Message: UiText.Get("Rep.Unexpected", "Unexpected answer from VirusTotal."));
        }
    }
}
