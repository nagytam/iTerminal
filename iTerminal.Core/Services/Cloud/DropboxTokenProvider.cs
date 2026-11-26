using System.Collections.Concurrent;
using System.Text.Json;
using iConsole.Services;
using Serilog;

namespace iTerminal.Core.Services.Cloud;

/// <summary>
/// Access tokens for <c>dropbox://</c>. Dropbox has no device-authorization grant, so the sign-in is
/// the user's: the refresh token of their Dropbox app is kept in the password manager (an entry whose
/// title starts with <c>Dropbox</c>, whose user name is the account label and whose password is the
/// refresh token). The app key comes from <c>Cloud:Dropbox:ClientId</c>; the refresh uses the PKCE
/// public-client form, so there is no client secret anywhere. Access tokens live in memory only and
/// are renewed a couple of minutes before they expire.
/// </summary>
public sealed class DropboxTokenProvider : ICloudTokenProvider, IDisposable
{
    /// <summary>The account label an empty <c>dropbox://</c> host means.</summary>
    public const string DefaultAccount = "default";

    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    private sealed record Grant(string AccessToken, DateTime ExpiresUtc);

    private readonly OAuthClientOptions _options;
    private readonly IPasswordManager? _passwords;
    private readonly ILogger? _logger;
    private readonly HttpClient _http;
    private readonly string _tokenEndpoint;
    private readonly ConcurrentDictionary<string, Grant> _grants = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DropboxTokenProvider(
        OAuthClientOptions options,
        IPasswordManager? passwords = null,
        ILogger? logger = null,
        HttpMessageHandler? handler = null,
        string tokenEndpoint = "https://api.dropboxapi.com/oauth2/token")
    {
        _options = options;
        _passwords = passwords;
        _logger = logger;
        _tokenEndpoint = tokenEndpoint;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
    }

    public async Task<string> GetAccessTokenAsync(string account, CancellationToken cancellationToken)
    {
        var label = string.IsNullOrWhiteSpace(account) ? DefaultAccount : account;
        if (_grants.TryGetValue(label, out var cached) && cached.ExpiresUtc - Margin > DateTime.UtcNow)
        {
            return cached.AccessToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_grants.TryGetValue(label, out cached) && cached.ExpiresUtc - Margin > DateTime.UtcNow)
            {
                return cached.AccessToken;
            }

            var grant = await RefreshAsync(label, cancellationToken).ConfigureAwait(false);
            _grants[label] = grant;
            return grant.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate(string account) => _grants.TryRemove(string.IsNullOrWhiteSpace(account) ? DefaultAccount : account, out _);

    public void InvalidateAll() => _grants.Clear();

    /// <summary>The refresh token for <paramref name="label"/>: the entry with that user name, or the first Dropbox entry for the default account.</summary>
    internal static string? PickRefreshToken(IEnumerable<(string Title, string Username, string Password)> entries, string label)
    {
        var dropbox = entries.Where(e => e.Title.StartsWith("Dropbox", StringComparison.OrdinalIgnoreCase) && e.Password.Length > 0).ToList();
        var entry = label == DefaultAccount
            ? dropbox.FirstOrDefault()
            : dropbox.FirstOrDefault(e => string.Equals(e.Username, label, StringComparison.Ordinal));
        return entry.Password is { Length: > 0 } token ? token : null;
    }

    private async Task<Grant> RefreshAsync(string label, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new CloudSignInRequiredException(
                "Dropbox is not set up: give the app key of your Dropbox app as Cloud:Dropbox:ClientId in appSettings.json.");
        }

        if (_passwords is null || !_passwords.IsUnlocked)
        {
            throw new CloudSignInRequiredException(
                "Unlock the password manager: the Dropbox refresh token is kept there.");
        }

        var entries = _passwords.List().Select(e => (e.Title, e.Username, e.Password));
        var refreshToken = PickRefreshToken(entries, label)
            ?? throw new CloudSignInRequiredException(
                "No Dropbox refresh token: add a password manager entry whose title starts with Dropbox, whose user name is the account and whose password is the refresh token.");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId,
        });
        using var response = await _http.PostAsync(_tokenEndpoint, form, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.Warning("Dropbox refused the refresh token for {Account}: HTTP {Status}", label, (int)response.StatusCode);
            throw new CloudSignInRequiredException(
                "Dropbox refused the refresh token: it was revoked or is wrong. Replace it in the password manager.");
        }

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new IOException("Dropbox did not return an access token.");
        var seconds = document.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var parsed) ? parsed : 14400;
        return new Grant(token, DateTime.UtcNow.AddSeconds(seconds));
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
