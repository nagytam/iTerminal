using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services.Evidence;

/// <summary>
/// The evidence notebook: a folder the person chooses, holding a copy of everything gathered into it
/// and an index that records where each entry came from. The folder is a plain folder on disk, so it
/// can be copied, archived or read without the application.
///
/// <para>As an <see cref="IFileSystem"/> it is read-only: the <c>evidence://</c> drive lists it, opens and
/// copies out of it, and never changes an entry. Entries are added through the typed methods below,
/// which record the origin in the same step.</para>
/// </summary>
public interface IEvidenceNotebookService : IFileSystem
{
    /// <summary>The folder of the open notebook, or null when none is open.</summary>
    string? FolderPath { get; }

    /// <summary>True when a notebook is open.</summary>
    bool IsOpen => FolderPath is not null;

    /// <summary>
    /// Opens the notebook in <paramref name="folder"/>, creating it there when the folder holds no index yet.
    /// The folder is created when it does not exist.
    /// </summary>
    /// <exception cref="InvalidDataException">The folder's index is not one this version can read.</exception>
    Task OpenAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>The entries in the order they were added.</summary>
    Task<IReadOnlyList<EvidenceRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Copies <paramref name="sourceUrl"/> from <paramref name="source"/> into the notebook and records its origin.</summary>
    Task<EvidenceRecord> AddFileAsync(IFileSystem source, Url sourceUrl, long? sourceSize, DateTime? sourceModifiedUtc, CancellationToken cancellationToken = default);

    /// <summary>Adds a note, a piece of clipboard text or a web address, as text.</summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not a text kind.</exception>
    Task<EvidenceRecord> AddTextAsync(EvidenceKind kind, string text, CancellationToken cancellationToken = default);

    /// <summary>Adds a recording of signed 16-bit little-endian PCM, kept as a WAV file.</summary>
    Task<EvidenceRecord> AddVoiceAsync(ReadOnlyMemory<byte> pcm, int sampleRate, CancellationToken cancellationToken = default);

    /// <summary>Re-reads every entry and reports those that are missing or no longer match their recorded digest and size.</summary>
    Task<IReadOnlyList<EvidenceProblem>> VerifyAsync(CancellationToken cancellationToken = default);
}
