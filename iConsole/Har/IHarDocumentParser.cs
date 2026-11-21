namespace iConsole.Har;

/// <summary>
/// Reads a HAR file. The implementation is in <c>iConsole.Formats</c>, so the HAR viewer here never references the JSON reading.
/// </summary>
public interface IHarDocumentParser
{
    /// <summary>
    /// Reads a HAR document. Throws <see cref="InvalidDataException"/> when the JSON has no <c>log.entries</c> list, and
    /// <see cref="System.Text.Json.JsonException"/> when the text is not JSON at all. Blocking CPU work: call it from a worker thread.
    /// </summary>
    HarDocument Parse(string json);
}
