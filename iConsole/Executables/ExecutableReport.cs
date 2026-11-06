namespace iConsole.Executables;

/// <summary>The executable formats the analysis view understands.</summary>
public enum ExecutableFormat
{
    /// <summary>A Windows Portable Executable: .exe, .dll, .sys and the like.</summary>
    PE,

    /// <summary>A Unix Executable and Linkable Format file.</summary>
    Elf,

    /// <summary>An Apple Mach-O file, thin or a universal ("fat") binary.</summary>
    MachO,
}

/// <summary>What kind of module the file is.</summary>
public enum ExecutableKind
{
    Unknown,
    Executable,
    PieExecutable,
    SharedLibrary,
    Object,
    Driver,
    Core,
    Bundle,
    Other,
}

/// <summary>
/// One hardening switch. <see cref="Enabled"/> is null when the switch does not apply to the file (SafeSEH on a 64-bit image) or
/// cannot be told (a stripped ELF has no symbol table to look for a stack canary in).
/// </summary>
public sealed record ExecutableFlag(string Name, bool? Enabled);

/// <summary>One section or segment: its name, its size in memory, its size in the file and the Shannon entropy of the bytes read (0 to 8).</summary>
/// <param name="Entropy">Bits per byte, or null when the section has no bytes in the file (bss) or none could be read.</param>
/// <param name="Sampled">True when only the first part of a very large section went into the entropy.</param>
public sealed record ExecutableSection(string Name, long Size, long FileSize, double? Entropy, bool Executable, bool Writable, bool Sampled);

/// <summary>An imported library and how many functions are taken from it; the count is -1 when the file does not say (ELF, Mach-O).</summary>
public sealed record ExecutableImport(string Library, int Functions);

/// <summary>A certificate in the embedded Authenticode signature. Read, not verified: no chain or revocation check was made.</summary>
public sealed record ExecutableCertificate(string Subject, string Issuer, DateTime NotBefore, DateTime NotAfter, string Thumbprint);

/// <summary>What looks suspicious or worth knowing; the dialog turns each into a sentence.</summary>
public enum ExecutableHintKind
{
    /// <summary>A section's bytes are close to random: compressed, encrypted or packed. Argument: the section name.</summary>
    HighEntropySection,

    /// <summary>A section name that a known packer uses. Argument: the section name.</summary>
    PackerSectionName,

    /// <summary>A section that can be both written and executed. Argument: the section name.</summary>
    WritableExecutableSection,

    /// <summary>An executable with almost no imports, as a packed one has.</summary>
    FewImports,

    /// <summary>A packer's signature in the file. Argument: the packer.</summary>
    PackerSignature,

    /// <summary>A section's entropy came from the first part of it only. Argument: the section name.</summary>
    EntropySampled,

    /// <summary>The file is damaged or cut short in a way that the parser worked around. Argument: what.</summary>
    Malformed,

    /// <summary>A limit stopped the listing (too many sections, imports or symbols). Argument: what.</summary>
    ListCut,

    /// <summary>The time stamp is in the future or zero, so it says nothing about the build.</summary>
    TimestampUnreliable,
}

public sealed record ExecutableHint(ExecutableHintKind Kind, string Argument = "");

/// <summary>Everything the analysis view shows about one file.</summary>
public sealed class ExecutableReport
{
    public required ExecutableFormat Format { get; init; }

    /// <summary>The format with its flavour: "PE32+ (64-bit)", "ELF64 little-endian", "Mach-O 64-bit".</summary>
    public required string FormatName { get; init; }

    /// <summary>The machine, such as "x86-64", "ARM64"; for a universal Mach-O binary, all slices separated by commas.</summary>
    public required string Architecture { get; init; }

    public ExecutableKind Kind { get; init; }

    /// <summary>The PE subsystem ("Windows console"), the ELF OS ABI or the Mach-O platform; empty when there is none.</summary>
    public string Subsystem { get; init; } = string.Empty;

    /// <summary>The build time stamp from the file header, or null when the format has none or it is unusable.</summary>
    public DateTime? Timestamp { get; init; }

    /// <summary>True for a .NET assembly (PE with a CLR header).</summary>
    public bool Managed { get; init; }

    public string EntryPoint { get; init; } = string.Empty;

    public IReadOnlyList<ExecutableFlag> Hardening { get; init; } = [];

    public IReadOnlyList<ExecutableImport> Imports { get; init; } = [];

    /// <summary>How many functions or symbols the file imports; -1 when unknown.</summary>
    public int ImportedSymbols { get; init; } = -1;

    public int ExportedSymbols { get; init; }

    /// <summary>The first few exported names.</summary>
    public IReadOnlyList<string> Exports { get; init; } = [];

    public IReadOnlyList<ExecutableSection> Sections { get; init; } = [];

    /// <summary>True when the file carries an embedded signature (Authenticode on PE, a code signature on Mach-O).</summary>
    public bool Signed { get; init; }

    public IReadOnlyList<ExecutableCertificate> Certificates { get; init; } = [];

    public IReadOnlyList<ExecutableHint> Hints { get; init; } = [];

    public long FileSize { get; init; }
}

/// <summary>A file that is not an executable of a known format, or is too damaged to read; the message is meant for the status line.</summary>
public sealed class ExecutableAnalysisException : Exception
{
    public ExecutableAnalysisException(string message) : base(message)
    {
    }

    public ExecutableAnalysisException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Reads an executable's headers for the analysis view, read-only: the file is parsed, never run. The implementation lives in
/// iTerminal.Core. The call blocks on file I/O, so callers run it on a worker thread.
/// </summary>
public interface IExecutableAnalyzer
{
    /// <exception cref="ExecutableAnalysisException">The file is not a readable executable.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    ExecutableReport Analyze(string path, CancellationToken cancellationToken);
}
