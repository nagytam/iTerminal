using System.Text.Json;

namespace iConsole.Har;

/// <summary>
/// One request and its response, as the grid lists it. <see cref="Source"/> is the entry's own JSON: the details read the
/// headers and bodies from it when a row is opened, so a large file does not copy every body up front.
/// </summary>
public sealed record HarEntry(
    int Number,
    string Method,
    string Url,
    int Status,
    string StatusText,
    string MimeType,
    long? Size,
    double TimeMs,
    string StartedDateTime,
    JsonElement Source);
