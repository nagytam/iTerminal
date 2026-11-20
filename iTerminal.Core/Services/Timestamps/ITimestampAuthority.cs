using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Services.Timestamps;

/// <summary>Obtains RFC 3161 time stamp tokens. The end-to-end suite replaces it, so no run reaches a time-stamp authority.</summary>
public interface ITimestampAuthority
{
    /// <summary>
    /// Asks for a token over <paramref name="sha256"/>. Null when no authority is configured.
    /// </summary>
    /// <exception cref="TimestampException">The authority could not be reached, refused, or answered with something that is not a valid token.</exception>
    Task<Rfc3161Timestamp?> TimestampAsync(byte[] sha256, CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks the authority named in the <see cref="SettingKey"/> setting over HTTP, as RFC 3161 requires: a POST of
/// a TimeStampReq, answered with a TimeStampResp. Only the SHA-256 digest leaves the machine, never the content.
/// An empty setting turns time stamping off.
/// </summary>
public sealed class Rfc3161TimestampAuthority : ITimestampAuthority, IDisposable
{
    /// <summary>The setting that names the time-stamp authority's address.</summary>
    public const string SettingKey = "EvidenceNotebook.TimestampAuthority";

    /// <summary>The authority used when the setting has never been set.</summary>
    public const string DefaultAuthority = "https://freetsa.org/tsr";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly ILogger? _logger;

    public Rfc3161TimestampAuthority(ISettingsService settings, ILogger? logger = null)
        : this(settings, new HttpClientHandler { UseCookies = false }, logger)
    {
    }

    /// <summary>Tests pass a handler that answers from memory.</summary>
    public Rfc3161TimestampAuthority(ISettingsService settings, HttpMessageHandler handler, ILogger? logger = null)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = RequestTimeout };
    }

    public void Dispose() => _http.Dispose();

    /// <inheritdoc />
    public async Task<Rfc3161Timestamp?> TimestampAsync(byte[] sha256, CancellationToken cancellationToken = default)
    {
        var address = (_settings.Get(SettingKey) ?? DefaultAuthority).Trim();
        if (address.Length == 0)
        {
            return null;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            throw new TimestampException($"“{address}” is not a web address for a time-stamp authority.");
        }

        var nonce = new BigInteger(RandomNumberGenerator.GetBytes(8), isUnsigned: true);
        try
        {
            using var content = new ByteArrayContent(Rfc3161.BuildRequest(sha256, nonce));
            content.Headers.ContentType = new MediaTypeHeaderValue(Rfc3161.RequestMediaType);
            using var response = await _http.PostAsync(uri, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return Rfc3161.ParseResponse(body, sha256, nonce, address);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // A timeout surfaces as a cancelled task; the caller's own cancellation is still an OperationCanceledException.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger?.Warning(exception, "Time-stamp authority {Authority} did not answer", address);
            throw new TimestampException($"The time-stamp authority {uri.Host} did not answer.", exception);
        }
    }
}
