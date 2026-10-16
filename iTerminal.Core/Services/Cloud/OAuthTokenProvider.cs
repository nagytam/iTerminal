using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using iConsole.Services;
using Serilog;

namespace iTerminal.Core.Services.Cloud;

/// <summary>
/// Settings for the cloud drives, bound from the <c>Cloud</c> section of <c>appSettings.json</c> (or
/// <c>Cloud__OneDrive__ClientId</c>-style environment variables). OneDrive and Google Drive need an
/// app registration of the user's own — iTerminal ships none, since a client id is the identity of
/// whoever registered it — so these start empty and the drives say how to fill them in.
/// </summary>
public class CloudOptions
{
    public OAuthClientOptions OneDrive { get; set; } = new();

    public OAuthClientOptions GoogleDrive { get; set; } = new();

    public S3Options S3 { get; set; } = new();
}

public class OAuthClientOptions
{
    /// <summary>The application (client) id of the registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>Google's installed-app client also has a secret; Microsoft's public client has none.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>OneDrive only: <c>common</c> (default), <c>consumers</c>, <c>organizations</c> or a tenant id.</summary>
    public string? Tenant { get; set; }
}

public class S3Options
{
    /// <summary>The region requests are signed for until a bucket says otherwise. Default <c>us-east-1</c>.</summary>
    public string? Region { get; set; }

    /// <summary>An S3-compatible server, e.g. <c>https://minio.example.com:9000</c>. Empty means Amazon S3.</summary>
    public string? Endpoint { get; set; }
}

/// <summary>
/// Raised when a drive cannot be used yet because the account is not signed in (or no app registration
/// is configured). The message is the instruction — it goes in the panel's footer as it is.
/// </summary>
public class CloudSignInRequiredException(string message) : IOException(message);

/// <summary>Hands out access tokens for one account of a cloud provider.</summary>
public interface ICloudTokenProvider
{
    /// <summary>A bearer token for <paramref name="account"/>; throws <see cref="CloudSignInRequiredException"/> when the user has to act first.</summary>
    Task<string> GetAccessTokenAsync(string account, CancellationToken cancellationToken);

    /// <summary>Forgets the cached access token (the service answered 401), so the next call refreshes it.</summary>
    void Invalidate(string account);
}

/// <summary>What one provider's OAuth device flow is made of.</summary>
public sealed record OAuthProviderConfig(
    string Name,
    string DeviceCodeEndpoint,
    string TokenEndpoint,
    string Scope,
    string? ClientId,
    string? ClientSecret,
    string ConfigurationHint);

/// <summary>
/// OAuth 2.0 for a terminal: the device authorization grant. The user is shown a short code and an
/// address, approves in any browser — on this machine or another — and the app polls until it is
/// granted; no redirect URI, no embedded browser. The refresh token is kept in the password manager
/// (sealed with the key the app generated) and only in memory while that is locked; access tokens never
/// leave memory.
///
/// <para>A sign-in cannot be waited for from a listing, so the first call starts the grant on a
/// background task, posts the code as a notification that stays until dismissed, and throws
/// <see cref="CloudSignInRequiredException"/> with the same words; the panel shows that in its
/// footer. Once the user has approved, the poll stores the tokens and posts "signed in", and the
/// next refresh (Ctrl+R) lists.</para>
/// </summary>
public sealed class OAuthTokenProvider : ICloudTokenProvider
{
    private sealed record Grant(string AccessToken, DateTime ExpiresUtc);

    private sealed record Pending(string Message);

    private readonly OAuthProviderConfig _config;
    private readonly IPasswordManager? _passwords;
    private readonly INotificationService? _notifications;
    private readonly ILogger? _logger;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Grant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _memoryRefreshTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OAuthTokenProvider(
        OAuthProviderConfig config,
        IPasswordManager? passwords = null,
        INotificationService? notifications = null,
        ILogger? logger = null,
        HttpMessageHandler? handler = null)
    {
        _config = config;
        _passwords = passwords;
        _notifications = notifications;
        _logger = logger;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
    }

    private string SecretId(string account) => $"cloud:{_config.Name.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal)}:{account}";

    private string? StoredRefreshToken(string account)
    {
        if (_memoryRefreshTokens.TryGetValue(account, out var token))
        {
            return token;
        }

        try
        {
            return _passwords?.GetSecret(SecretId(account));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private void StoreRefreshToken(string account, string token)
    {
        _memoryRefreshTokens[account] = token;
        try
        {
            if (_passwords is { IsUnlocked: true })
            {
                _passwords.SaveSecret(SecretId(account), token);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            _logger?.Warning(exception, "Could not save the {Provider} refresh token", _config.Name);
        }
    }

    private void ForgetRefreshToken(string account)
    {
        _memoryRefreshTokens.TryRemove(account, out _);
        try
        {
            if (_passwords is { IsUnlocked: true })
            {
                _passwords.DeleteSecret(SecretId(account));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Nothing to forget.
        }
    }

    public void Invalidate(string account) => _grants.TryRemove(account, out _);

    public async Task<string> GetAccessTokenAsync(string account, CancellationToken cancellationToken)
    {
        if (_grants.TryGetValue(account, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return cached.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(_config.ClientId))
        {
            throw new CloudSignInRequiredException($"{_config.Name} needs an app registration: {_config.ConfigurationHint}");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_grants.TryGetValue(account, out cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return cached.AccessToken;
            }

            if (StoredRefreshToken(account) is { Length: > 0 } refreshToken)
            {
                var refreshed = await RedeemAsync(account, new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["scope"] = _config.Scope,
                }, cancellationToken).ConfigureAwait(false);
                if (refreshed is not null)
                {
                    return refreshed;
                }

                // The refresh token was refused (revoked, expired): the user signs in again.
                ForgetRefreshToken(account);
            }

            if (_pending.TryGetValue(account, out var pending))
            {
                throw new CloudSignInRequiredException(pending.Message);
            }

            throw await StartSignInAsync(account, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Posts the token request; null when the server refused the grant, the access token otherwise.</summary>
    private async Task<string?> RedeemAsync(string account, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        form["client_id"] = _config.ClientId!;
        if (!string.IsNullOrEmpty(_config.ClientSecret))
        {
            form["client_secret"] = _config.ClientSecret;
        }

        using var response = await _http.PostAsync(_config.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = ParseJson(text);
        if (!response.IsSuccessStatusCode || document is null || !document.RootElement.TryGetProperty("access_token", out var accessToken))
        {
            return null;
        }

        var expires = document.RootElement.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var seconds)
            ? seconds
            : 3600;
        var token = accessToken.GetString()!;
        _grants[account] = new Grant(token, DateTime.UtcNow.AddSeconds(expires));
        if (document.RootElement.TryGetProperty("refresh_token", out var refresh) && refresh.GetString() is { Length: > 0 } newRefresh)
        {
            StoreRefreshToken(account, newRefresh);
        }

        return token;
    }

    private static JsonDocument? ParseJson(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<CloudSignInRequiredException> StartSignInAsync(string account, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _config.ClientId!,
            ["scope"] = _config.Scope,
        };
        using var response = await _http.PostAsync(_config.DeviceCodeEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = ParseJson(text);
        if (!response.IsSuccessStatusCode || document is null || !document.RootElement.TryGetProperty("device_code", out var deviceCodeElement))
        {
            var reason = document is not null && document.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : $"HTTP {(int)response.StatusCode}";
            return new CloudSignInRequiredException($"{_config.Name} sign-in could not start: {reason}");
        }

        var root = document.RootElement;
        var deviceCode = deviceCodeElement.GetString()!;
        var userCode = root.GetProperty("user_code").GetString()!;
        var address = (root.TryGetProperty("verification_uri", out var uri) ? uri.GetString()
            : root.TryGetProperty("verification_url", out var url) ? url.GetString() : null) ?? "the provider's device login page";
        var interval = root.TryGetProperty("interval", out var intervalElement) && intervalElement.TryGetInt32(out var i) ? i : 5;
        var lifetime = root.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var e) ? e : 900;

        var message = $"Sign in to {_config.Name}: open {address} and enter {userCode}, then refresh (Ctrl+R).";
        _pending[account] = new Pending(message);
        _notifications?.Notify($"Sign in to {_config.Name}", $"Open {address} and enter {userCode}", iConsole.Services.NotificationSeverity.Info, Timeout.InfiniteTimeSpan);

        _ = Task.Run(() => PollAsync(account, deviceCode, TimeSpan.FromSeconds(Math.Max(1, interval)), TimeSpan.FromSeconds(lifetime)));
        return new CloudSignInRequiredException(message);
    }

    private async Task PollAsync(string account, string deviceCode, TimeSpan interval, TimeSpan lifetime)
    {
        var deadline = DateTime.UtcNow + lifetime;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(interval).ConfigureAwait(false);
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = deviceCode,
                    ["client_id"] = _config.ClientId!,
                };
                if (!string.IsNullOrEmpty(_config.ClientSecret))
                {
                    form["client_secret"] = _config.ClientSecret;
                }

                using var response = await _http.PostAsync(_config.TokenEndpoint, new FormUrlEncodedContent(form)).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var document = ParseJson(text);
                if (document is null)
                {
                    continue;
                }

                if (response.IsSuccessStatusCode && document.RootElement.TryGetProperty("access_token", out var accessToken))
                {
                    var seconds = document.RootElement.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var s) ? s : 3600;
                    _grants[account] = new Grant(accessToken.GetString()!, DateTime.UtcNow.AddSeconds(seconds));
                    if (document.RootElement.TryGetProperty("refresh_token", out var refresh) && refresh.GetString() is { Length: > 0 } token)
                    {
                        StoreRefreshToken(account, token);
                    }

                    _notifications?.Notify($"Signed in to {_config.Name}", "Refresh the panel (Ctrl+R) to list it.", iConsole.Services.NotificationSeverity.Success);
                    return;
                }

                var error = document.RootElement.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                switch (error)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval += TimeSpan.FromSeconds(5);
                        continue;
                    default:
                        _notifications?.Notify($"{_config.Name} sign-in failed", error ?? "The request was refused.", iConsole.Services.NotificationSeverity.Error);
                        return;
                }
            }

            _notifications?.Notify($"{_config.Name} sign-in expired", "Open the drive again to get a new code.", iConsole.Services.NotificationSeverity.Warning);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger?.Warning(exception, "{Provider} sign-in polling failed", _config.Name);
        }
        finally
        {
            _pending.TryRemove(account, out _);
        }
    }

    /// <summary>Microsoft Graph (OneDrive, personal and work or school).</summary>
    public static OAuthProviderConfig OneDrive(OAuthClientOptions options)
    {
        var tenant = string.IsNullOrWhiteSpace(options.Tenant) ? "common" : options.Tenant.Trim();
        return new OAuthProviderConfig(
            "OneDrive",
            $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/devicecode",
            $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
            "Files.ReadWrite.All offline_access",
            options.ClientId,
            options.ClientSecret,
            "register an app in Microsoft Entra (Authentication > allow public client flows, Files.ReadWrite.All) and set Cloud:OneDrive:ClientId in appSettings.json.");
    }

    /// <summary>Google Drive, full access (an app created for "TVs and limited-input devices").</summary>
    public static OAuthProviderConfig GoogleDrive(OAuthClientOptions options) => new(
        "Google Drive",
        "https://oauth2.googleapis.com/device/code",
        "https://oauth2.googleapis.com/token",
        "https://www.googleapis.com/auth/drive",
        options.ClientId,
        options.ClientSecret,
        "create an OAuth client of type TVs and limited-input devices in Google Cloud, enable the Drive API, and set Cloud:GoogleDrive:ClientId and ClientSecret in appSettings.json.");

    internal static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
