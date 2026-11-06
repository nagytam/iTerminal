namespace iTerminal.Core.Services.Streams;

/// <summary>
/// The metadata a file carries beside its contents: NTFS alternate data streams on Windows, extended attributes
/// on Linux and macOS. Every member is async by contract — it is disk I/O, and none runs from a key handler.
/// A failure of the call itself (no such file, a volume without support, access denied) comes back as a sentence
/// in the result, never as an exception; only the caller's own cancellation is thrown. Local paths only.
/// </summary>
public interface IStreamAttributeService
{
    /// <summary>The word for what this system calls them, shown in the dialog title: "Streams" or "Attributes".</summary>
    string Kind { get; }

    /// <summary>The streams or attributes of <paramref name="path"/>; the unnamed main stream is not one of them.</summary>
    Task<StreamListing> ListAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The first <paramref name="maxBytes"/> bytes of the named stream or attribute.</summary>
    Task<StreamData> ReadAsync(string path, string name, int maxBytes, CancellationToken cancellationToken = default);

    /// <summary>Removes the named stream or attribute. The file's contents are not touched.</summary>
    Task<StreamResult> DeleteAsync(string path, string name, CancellationToken cancellationToken = default);
}
