using System.Net;
using System.Text;

namespace iTerminal.Core.Test.Services.Cloud;

/// <summary>One request a fake server saw.</summary>
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body)
{
    public string Text => Encoding.UTF8.GetString(Body);

    /// <summary>The query string, decoded, for matching on what was asked.</summary>
    public string Query => Uri.UnescapeDataString(Uri.Query.Replace('+', ' '));

    public string? Header(string name) => Headers.GetValueOrDefault(name);
}

/// <summary>A server that answers each request from a function and remembers it.</summary>
internal sealed class FakeHttpHandler(Func<SeenRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<SeenRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        byte[] body = [];
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var seen = new SeenRequest(request.Method, request.RequestUri!, headers, body);
        lock (Requests)
        {
            Requests.Add(seen);
        }

        return respond(seen);
    }

    public static HttpResponseMessage Reply(HttpStatusCode status, string body = "", string mediaType = "application/xml") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
