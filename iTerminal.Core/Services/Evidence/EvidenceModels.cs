using System.Text.Json.Serialization;

namespace iTerminal.Core.Services.Evidence;

/// <summary>What kind of thing an evidence entry holds.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EvidenceKind
{
    /// <summary>A file copied from a panel, of any type.</summary>
    File,

    /// <summary>A note typed into the notebook.</summary>
    Note,

    /// <summary>A recording from the microphone, kept as a WAV file.</summary>
    Voice,

    /// <summary>Text taken from the clipboard.</summary>
    Clipboard,

    /// <summary>A web address, kept as text.</summary>
    Link,
}

/// <summary>
/// Where an entry came from and where it was gathered, written automatically when the entry is
/// added. Nothing here is typed by the person: a note's origin is the machine and account it was
/// written on, a file's origin is the path it was read from as well.
/// </summary>
public sealed record EvidenceOrigin(
    string SourceUrl,
    string SourceDrive,
    long? SourceSize,
    DateTime? SourceModifiedUtc,
    string Machine,
    string OperatingUser,
    string OperatingSystem,
    string AppVersion,
    IReadOnlyList<string> LocalAddresses,
    DateTime CapturedUtc,
    string UtcOffset);

/// <summary>
/// One entry of the notebook: its content file, what it is, its digest, and where it came from.
/// <see cref="FileName"/> is the content file's name inside the notebook's <c>items</c> folder and
/// doubles as the entry's name on the <c>evidence://</c> drive.
/// </summary>
public sealed record EvidenceRecord(
    int Sequence,
    string FileName,
    EvidenceKind Kind,
    string Title,
    long Size,
    string Sha256,
    EvidenceOrigin Origin,
    double? DurationSeconds = null);

/// <summary>
/// The notebook's index, <c>notebook.json</c> in the notebook folder. It is rewritten whole on every
/// change, through a temporary file, so a crash leaves either the old index or the new one.
/// </summary>
public sealed record EvidenceNotebookIndex(
    int Format,
    string Id,
    EvidenceOrigin Creator,
    List<EvidenceRecord> Entries)
{
    /// <summary>The only format this version writes and reads.</summary>
    public const int CurrentFormat = 1;
}

/// <summary>A problem <see cref="IEvidenceNotebookService.VerifyAsync"/> found with one entry.</summary>
public sealed record EvidenceProblem(string FileName, string Message);
