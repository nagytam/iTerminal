namespace iTerminal.Core.Services.Archives;

/// <summary>How a password-protected archive encrypts its contents.</summary>
public enum ArchiveEncryption
{
    /// <summary>AES-256: what a password should mean. Opened by 7-Zip, WinRAR and macOS, not by Windows Explorer.</summary>
    Aes256,

    /// <summary>The original PKZIP cipher: weak, but every unzip tool — Windows Explorer included — opens it.</summary>
    ZipCrypto,
}

/// <summary>Which writer produces a format.</summary>
public enum ArchiveWriterKind
{
    /// <summary>Written in-process (SharpZipLib).</summary>
    Managed,

    /// <summary>Written by the 7-Zip command-line tool, which has to be installed.</summary>
    SevenZip,
}

/// <summary>
/// An archive format the Compress dialog (Alt+F5) can write, and what it allows. A format is only
/// offered with the settings its writer can honour: TAR has no compression level and no format
/// in the TAR family has a password, so the dialog disables those fields instead of accepting a
/// value that would be silently ignored.
/// </summary>
/// <param name="Name">What the format list shows.</param>
/// <param name="Extensions">File-name endings that mean this format, the one a new archive gets first.</param>
/// <param name="MinLevel">Lowest compression level; equal to <paramref name="MaxLevel"/> when the format has none.</param>
/// <param name="LevelHint">What the levels mean, for the dialog's hint line.</param>
/// <param name="Encryptions">The ciphers a password can use; empty when the format takes no password.</param>
/// <param name="CanEncryptNames">Whether the listing itself can be hidden behind the password (7z's header encryption).</param>
public sealed record ArchiveFormat(
    string Name,
    IReadOnlyList<string> Extensions,
    ArchiveWriterKind Writer,
    int MinLevel,
    int MaxLevel,
    int DefaultLevel,
    string LevelHint,
    IReadOnlyList<ArchiveEncryption> Encryptions,
    bool CanEncryptNames)
{
    public string Extension => Extensions[0];

    public bool HasLevels => MaxLevel > MinLevel;

    public bool SupportsPassword => Encryptions.Count > 0;

    public static readonly ArchiveFormat Zip = new(
        "ZIP", [".zip"], ArchiveWriterKind.Managed, 0, 9, 6,
        "0 stores, 1 is fastest, 9 is smallest",
        [ArchiveEncryption.Aes256, ArchiveEncryption.ZipCrypto], CanEncryptNames: false);

    public static readonly ArchiveFormat SevenZip = new(
        "7z", [".7z"], ArchiveWriterKind.SevenZip, 0, 9, 5,
        "0 stores, 1 is fastest, 9 is ultra",
        [ArchiveEncryption.Aes256], CanEncryptNames: true);

    public static readonly ArchiveFormat TarGz = new(
        "TAR.GZ", [".tar.gz", ".tgz"], ArchiveWriterKind.Managed, 1, 9, 6,
        "1 is fastest, 9 is smallest",
        [], CanEncryptNames: false);

    public static readonly ArchiveFormat TarBz2 = new(
        "TAR.BZ2", [".tar.bz2", ".tbz2", ".tbz"], ArchiveWriterKind.Managed, 1, 9, 9,
        "1 is fastest, 9 is smallest",
        [], CanEncryptNames: false);

    public static readonly ArchiveFormat Tar = new(
        "TAR", [".tar"], ArchiveWriterKind.Managed, 0, 0, 0,
        "TAR does not compress",
        [], CanEncryptNames: false);

    /// <summary>Every format the app can write, the default first.</summary>
    public static IReadOnlyList<ArchiveFormat> All { get; } = [Zip, SevenZip, TarGz, TarBz2, Tar];

    /// <summary>
    /// The format a file name says it is, or null. Longest ending first, so <c>x.tar.gz</c> is a
    /// TAR.GZ and not a TAR that happens to end in <c>.gz</c>.
    /// </summary>
    public static ArchiveFormat? FromFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return All
            .SelectMany(format => format.Extensions.Select(extension => (format, extension)))
            .OrderByDescending(pair => pair.extension.Length)
            .FirstOrDefault(pair => path.EndsWith(pair.extension, StringComparison.OrdinalIgnoreCase))
            .format;
    }

    /// <summary><paramref name="path"/> with whichever archive ending it has replaced by this format's.</summary>
    public string ApplyExtension(string path)
    {
        if (FromFileName(path) is { } current)
        {
            var ending = current.Extensions
                .OrderByDescending(e => e.Length)
                .First(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            path = path[..^ending.Length];
        }

        return path + Extension;
    }

    public override string ToString() => Name;
}
