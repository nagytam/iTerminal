using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Audio;
using iTerminal.Core.Services.Music;
using System.Text;

namespace iTerminal.Core.Services.Preview;

/// <summary>What the preview panel shows for one entry.</summary>
/// <param name="Title">The panel title: what is being previewed.</param>
/// <param name="Summary">One line for the footer: size, line count, kind.</param>
/// <param name="Lines">The body, one panel row each, already fitted to the width it was built for.</param>
public sealed record FilePreview(string Title, string Summary, IReadOnlyList<string> Lines)
{
    public static FilePreview Message(string title, string message) => new(title, string.Empty, [string.Empty, " " + message]);

    /// <summary>Open the preview on its last row rather than its first — a log, whose news is at the bottom.</summary>
    public bool StartAtEnd { get; init; }
}

/// <summary>
/// A panel whose rows are not all files, and which knows better than the file preview what to show
/// for some of them: the docker drive answers for a container row with its log. Asked first; null
/// means "a file or folder like any other", and the ordinary preview is built.
/// </summary>
public interface IEntryPreviewSource
{
    Task<FilePreview?> BuildPreviewAsync(IEntry entry, int width, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the quick-view preview of a file or folder: the first part of a text file, a hex dump of
/// a binary one, a summary of a folder, the tags of a track, and — for PDF, Office documents,
/// HTML and images — whatever F3's viewer would render, through the same
/// <see cref="IFileToTextConverterService"/>, so the two never disagree about what a file holds.
///
/// <para>A preview is a glance, not a viewer: it reads at most <see cref="MaxTextBytes"/> of a text
/// file and <see cref="MaxRenderedBytes"/> of a document, and stops at <see cref="MaxLines"/> rows.
/// That bound is the point — the preview follows the cursor, and every step of the cursor over a
/// multi-gigabyte log must cost the same as a step over a README.</para>
///
/// <para>It reads through the panel's own <see cref="IFileSystem"/>, so a preview works on every
/// drive that can stream a file: a commit on the git drive, a container on the docker drive, an
/// SSH folder. Only the document converters need a local path (they open the file themselves) and
/// are used only for local files.</para>
/// </summary>
public sealed class FilePreviewService(
    IFileToTextConverterService? fileToTextConverterService = null,
    IMusicMetadataService? musicMetadataService = null) : IFilePreviewService
{
    public const int MaxTextBytes = 64 * 1024;
    public const long MaxRenderedBytes = 32L * 1024 * 1024;
    public const int MaxLines = 2_000;
    private const int HexBytes = 4 * 1024;
    private const int MaxFolderRows = 500;

    private static readonly HashSet<string> RenderedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "docx", "xlsx", "xlsm", "xltx", "xltm", "pptx", "pptm", "potx", "potm",
        "html", "htm",
        "bmp", "png", "jpg", "jpeg", "gif", "tiff", "tif", "webp", "ico",
        "dll", "exe", "so",
    };

    /// <summary>Of the rendered kinds, these read through the file system rather than a local path.</summary>
    private static readonly HashSet<string> RenderedFromStream = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "htm", "bmp", "png", "jpg", "jpeg", "gif", "tiff", "tif", "webp", "ico",
    };

    public async Task<FilePreview> BuildAsync(IEntry? entry, IFileSystem? fileSystem, int width, CancellationToken cancellationToken)
    {
        width = Math.Max(10, width);
        if (entry is null)
        {
            return FilePreview.Message("Preview", "Nothing under the cursor.");
        }

        var url = entry.Url;
        var name = entry.DisplayName;
        if (name == "..")
        {
            return FilePreview.Message("Preview", "The way up — move the cursor onto a file or folder.");
        }

        try
        {
            if (fileSystem is IEntryPreviewSource source
                && await source.BuildPreviewAsync(entry, width, cancellationToken) is { } own)
            {
                return own;
            }

            if (url.PathType == PathType.Folder)
            {
                return await BuildFolderAsync(url, name, fileSystem, width, cancellationToken);
            }

            var isLocal = url.ContainerType == ContainerType.FileSystem;
            if (isLocal && !File.Exists(url.FullName))
            {
                return FilePreview.Message(name, "The file is gone.");
            }

            var size = isLocal ? new FileInfo(url.FullName).Length : (entry as Model.ContentModels.VirtualDriveEntry)?.Size ?? url.Length;

            if (isLocal && musicMetadataService is not null && AudioDecoderFactory.IsAudioFile(url.FullName))
            {
                return await BuildAudioAsync(url, name, width, cancellationToken);
            }

            if (fileToTextConverterService is not null
                && fileSystem is not null
                && RenderedExtensions.Contains(url.Extension)
                && (isLocal || RenderedFromStream.Contains(url.Extension))
                && size <= MaxRenderedBytes)
            {
                var rendered = await BuildRenderedAsync(url, name, size, fileSystem, width);
                if (rendered is not null)
                {
                    return rendered;
                }
            }

            var head = await ReadHeadAsync(url, fileSystem, cancellationToken);
            return LooksBinary(head)
                ? BuildHexDump(name, size, head)
                : BuildText(name, size, head, width);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return FilePreview.Message(name, $"Cannot preview: {exception.Message}");
        }
    }

    #region Folders

    private static async Task<FilePreview> BuildFolderAsync(Url url, string name, IFileSystem? fileSystem, int width, CancellationToken cancellationToken)
    {
        List<string> lines = [string.Empty];
        if (url.ContainerType == ContainerType.FileSystem)
        {
            var directory = new DirectoryInfo(url.FullName);
            if (!directory.Exists)
            {
                return FilePreview.Message(name, "The folder is gone.");
            }

            List<FileSystemInfo> children;
            try
            {
                children = [.. directory.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true })];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return FilePreview.Message(name, $"Cannot read the folder: {exception.Message}");
            }

            var folders = children.OfType<DirectoryInfo>().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var files = children.OfType<FileInfo>().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var bytes = files.Sum(f => f.Length);
            var newest = children.Count > 0 ? children.Max(c => c.LastWriteTime) : directory.LastWriteTime;

            lines.Add($" {Plural(folders.Count, "folder")}, {Plural(files.Count, "file")}, {bytes.ToFileSize()} in the files here");
            lines.Add($" Last change: {newest:yyyy-MM-dd HH:mm}");
            lines.Add(string.Empty);
            foreach (var folder in folders.Take(MaxFolderRows))
            {
                lines.Add(Fit($" {folder.Name}{Path.DirectorySeparatorChar}", width));
            }

            foreach (var file in files.Take(Math.Max(0, MaxFolderRows - folders.Count)))
            {
                lines.Add(FitColumns($" {file.Name}", file.Length.ToFileSize(), width));
            }

            if (children.Count > MaxFolderRows)
            {
                lines.Add($" … and {children.Count - MaxFolderRows:N0} more");
            }

            return new FilePreview(name, $"{Plural(folders.Count, "folder")}, {Plural(files.Count, "file")}", lines);
        }

        if (fileSystem is IFolderListingFileSystem listing)
        {
            var children = await Task.Run(() => listing.ListChildren(url).ToList(), cancellationToken);
            foreach (var child in children.Take(MaxFolderRows))
            {
                lines.Add(Fit($" {child.Name}{(child.PathType == PathType.Folder ? "/" : string.Empty)}", width));
            }

            return new FilePreview(name, Plural(children.Count, "item"), lines);
        }

        return FilePreview.Message(name, "A folder — press Enter in the other panel to open it.");
    }

    #endregion

    #region Files

    private async Task<FilePreview> BuildAudioAsync(Url url, string name, int width, CancellationToken cancellationToken)
    {
        var metadata = await musicMetadataService!.ReadAsync(url.FullName, cancellationToken);
        List<string> lines = [string.Empty];
        var tags = metadata.TagSet;
        if (tags.Title.Length > 0) lines.Add(Fit($" Title:  {tags.Title}", width));
        if (tags.Artist.Length > 0) lines.Add(Fit($" Artist: {tags.Artist}", width));
        if (tags.Album.Length > 0) lines.Add(Fit($" Album:  {tags.Album}", width));
        if (metadata.Duration > TimeSpan.Zero) lines.Add($" Length: {FormatDuration(metadata.Duration)}");
        if (metadata.Format.Length > 0) lines.Add(Fit($" Format: {metadata.Format}", width));
        lines.Add(string.Empty);
        foreach (var tag in tags.Tags)
        {
            lines.Add(Fit($" {tag.Name}: {tag.Value}", width));
        }

        return new FilePreview(name, $"{metadata.Size.ToFileSize()} · audio · F3 plays it", lines);
    }

    private async Task<FilePreview?> BuildRenderedAsync(Url url, string name, long size, IFileSystem fileSystem, int width)
    {
        var textModel = new TextModel();
        await fileToTextConverterService!.ReadFileAsync(url, fileSystem, textModel);
        var source = textModel.Lines.ToList();
        if (source.Count == 0 || source.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        // Rendered documents are prose: wrapped at word boundaries, unlike source code, which is
        // cut at the edge the way an editor shows it.
        List<string> lines = [];
        foreach (var line in source)
        {
            if (line.Length <= width)
            {
                lines.Add(line);
            }
            else
            {
                lines.AddRange(line.WrapWords(width));
            }

            if (lines.Count >= MaxLines)
            {
                break;
            }
        }

        return new FilePreview(name, $"{size.ToFileSize()} · {url.Extension.ToUpperInvariant()}", lines);
    }

    private static async Task<byte[]> ReadHeadAsync(Url url, IFileSystem? fileSystem, CancellationToken cancellationToken)
    {
        if (url.ContainerType == ContainerType.FileSystem)
        {
            await using var file = new FileStream(url.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaxTextBytes];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await file.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
            {
                total += read;
            }

            return buffer[..total];
        }

        if (fileSystem is null)
        {
            throw new IOException("This drive cannot be read.");
        }

        // A remote file is streamed into a buffer that stops taking bytes at the limit: the preview
        // needs the head, and asking for the whole file would move a large one over the wire.
        var capped = new CappedStream(MaxTextBytes);
        try
        {
            await fileSystem.StreamToAsync(url, capped, progress: null, cancellationToken);
        }
        catch (CappedStream.LimitReachedException)
        {
        }
        catch (Exception exception) when (exception.InnerException is CappedStream.LimitReachedException)
        {
        }
        catch (NotImplementedException)
        {
            // A drive that cannot stream (SSH, SMB) still reads a file whole.
            var file = await fileSystem.ReadFileAsync(url);
            var content = file?.Content ?? [];
            return content.Length > MaxTextBytes ? content[..MaxTextBytes] : content;
        }

        return capped.ToArray();
    }

    /// <summary>NUL bytes, or more than a tenth control characters, outside a UTF-16 byte-order mark.</summary>
    internal static bool LooksBinary(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
        {
            return false;
        }

        var sample = head[..Math.Min(head.Length, 8000)];
        if (sample.IndexOf((byte)0) >= 0)
        {
            return true;
        }

        var control = 0;
        foreach (var b in sample)
        {
            if (b < 0x20 && b is not (byte)'\t' and not (byte)'\n' and not (byte)'\r' and not 0x0C and not 0x1B)
            {
                control++;
            }
        }

        return sample.Length > 0 && control * 10 > sample.Length;
    }

    internal static FilePreview BuildText(string name, long size, byte[] head, int width)
    {
        var text = Decode(head, out var encodingName);
        var lines = new List<string>();
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lines.Add(Fit(Clean(raw), width));
            if (lines.Count >= MaxLines)
            {
                break;
            }
        }

        var truncated = size > head.Length || lines.Count >= MaxLines;
        if (truncated)
        {
            lines.Add(string.Empty);
            lines.Add($" … the preview stops here — F3 shows the whole file");
        }

        var summary = $"{size.ToFileSize()} · {encodingName}{(truncated ? " · first part" : $" · {Plural(lines.Count, "line")}")}";
        return new FilePreview(name, summary, lines);
    }

    internal static FilePreview BuildHexDump(string name, long size, byte[] head)
    {
        var bytes = head.AsSpan(0, Math.Min(head.Length, HexBytes));
        List<string> lines = [];
        var builder = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            builder.Clear();
            builder.Append(offset.ToString("X8")).Append("  ");
            var row = bytes.Slice(offset, Math.Min(16, bytes.Length - offset));
            for (var i = 0; i < 16; i++)
            {
                builder.Append(i < row.Length ? row[i].ToString("X2") : "  ").Append(i == 7 ? "  " : " ");
            }

            builder.Append(' ');
            foreach (var b in row)
            {
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            lines.Add(builder.ToString());
        }

        if (size > bytes.Length)
        {
            lines.Add(string.Empty);
            lines.Add($" … {size - bytes.Length:N0} more bytes");
        }

        return new FilePreview(name, $"{size.ToFileSize()} · binary", lines);
    }

    /// <summary>
    /// UTF-8 unless a byte-order mark says otherwise; bytes that are not valid UTF-8 are read as
    /// Latin-1 rather than shown as replacement characters, the choice the document indexer makes.
    /// </summary>
    private static string Decode(byte[] head, out string encodingName)
    {
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            encodingName = "UTF-16";
            return Encoding.Unicode.GetString(head, 2, head.Length - 2);
        }

        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            encodingName = "UTF-16 BE";
            return Encoding.BigEndianUnicode.GetString(head, 2, head.Length - 2);
        }

        var start = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
        var length = head.Length - start;
        encodingName = "UTF-8";
        if (length == 0)
        {
            return string.Empty;
        }

        // A head cut at the read limit may end inside a character; up to three trailing bytes of
        // such a buffer are allowed to be invalid. Anywhere else, invalid UTF-8 means another encoding.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var maxTrim = head.Length == MaxTextBytes ? Math.Min(3, length - 1) : 0;
        for (var trim = 0; trim <= maxTrim; trim++)
        {
            try
            {
                return strict.GetString(head, start, length - trim);
            }
            catch (DecoderFallbackException)
            {
            }
        }

        encodingName = "Latin-1";
        return Encoding.Latin1.GetString(head, start, length);
    }

    /// <summary>Tabs to four spaces, other control characters to nothing — they would move the terminal's cursor.</summary>
    private static string Clean(string line)
    {
        if (!line.Any(char.IsControl))
        {
            return line;
        }

        var builder = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (c == '\t')
            {
                builder.Append(' ', 4 - builder.Length % 4);
            }
            else if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    #endregion

    #region Formatting

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes}:{value.Seconds:00}";

    private static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}";

    internal static string Fit(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private static string FitColumns(string left, string right, int width)
    {
        var room = width - right.Length - 1;
        if (room < 4)
        {
            return Fit(left, width);
        }

        return Fit(left, room).PadRight(room) + " " + right;
    }

    #endregion

    /// <summary>A memory stream that refuses bytes past a limit by throwing, which ends the copy feeding it.</summary>
    private sealed class CappedStream(int limit) : MemoryStream
    {
        public sealed class LimitReachedException : IOException
        {
        }

        // Every overload funnels into the array one: MemoryStream's span overload, in a derived
        // class, calls back into Write(byte[], int, int), so overriding both in terms of each other
        // recurses forever.
        public override void Write(byte[] buffer, int offset, int count)
        {
            var room = limit - (int)Length;
            base.Write(buffer, offset, Math.Min(room, count));
            if (count > room)
            {
                throw new LimitReachedException();
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.ToArray(), 0, buffer.Length);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>See <see cref="FilePreviewService"/>.</summary>
public interface IFilePreviewService
{
    /// <summary>
    /// Builds the preview of <paramref name="entry"/>, read through <paramref name="fileSystem"/>, with
    /// rows fitted to <paramref name="width"/>. Never throws for an unreadable file — the reason is
    /// the preview — only for cancellation.
    /// </summary>
    Task<FilePreview> BuildAsync(IEntry? entry, IFileSystem? fileSystem, int width, CancellationToken cancellationToken);
}
