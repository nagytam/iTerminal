using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Processes;
using System.Formats.Tar;
using System.IO.Pipelines;
using System.Globalization;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Docker;

/// <summary>
/// The <c>docker://</c> drive: every container on the machine at the root, and each container's
/// file system below it — <c>docker://&lt;container&gt;/etc/nginx/</c>.
///
/// <para>Everything goes through the docker CLI rather than the Engine API, for the reason the
/// audio decoders shell out to ffmpeg: the CLI already knows how to reach the daemon on every
/// platform (named pipe, socket, context, remote host), and the app should not re-learn that.</para>
///
/// <para>Two different mechanisms, chosen by what each needs. <b>Listing</b> runs <c>ls</c> inside
/// the container (<c>docker exec</c>): it is the only way to read one folder without copying the
/// whole subtree, and <c>-lAnL</c> is the subset GNU coreutils and BusyBox agree on — numeric ids
/// (no passwd lookup), symlinks followed so <c>/bin → usr/bin</c> lists as the folder it is.
/// <b>File contents</b> go through <c>docker cp</c>, which speaks tar on stdin/stdout and works on a
/// stopped container and on an image with no shell at all, where <c>cat</c> would not.</para>
/// </summary>
public sealed partial class DockerDriveService(IExternalProcessRunner processRunner) : IDockerDriveService
{
    private const string Docker = "docker";

    public bool IsReadOnly => false;

    #region Listing

    public async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var (container, path) = Split(url);
        return container.Length == 0
            ? await ListContainersAsync(cancellationToken)
            : await ListFolderAsync(container, path, cancellationToken);
    }

    private async Task<IReadOnlyList<IEntry>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            Docker,
            ["ps", "-a", "--format", "{{.Names}}\t{{.State}}\t{{.Status}}\t{{.Image}}\t{{.CreatedAt}}"],
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        return ParseContainers(result.StandardOutput);
    }

    internal static List<IEntry> ParseContainers(string output)
    {
        List<IEntry> running = [];
        List<IEntry> others = [];
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 5 || fields[0].Length == 0)
            {
                continue;
            }

            var state = fields[1].ToLowerInvariant();
            DateTime? created = DateTime.TryParseExact(
                fields[4].Length >= 19 ? fields[4][..19] : fields[4],
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed) ? parsed : null;

            // A container can have several names, comma-separated; the first is the one to address it by.
            var name = fields[0].Split(',')[0];
            var entry = new DockerContainerEntry(name, state, fields[2], fields[3], created);
            (state == "running" ? running : others).Add(entry);
        }

        // Running containers first: they are the ones that can be browsed.
        return [.. running, .. others];
    }

    /// <summary>Six characters, the width of the size column.</summary>
    internal static string ShortState(string state) => state switch
    {
        "running" => "Up",
        "exited" => "Exited",
        "paused" => "Paused",
        "created" => "New",
        "restarting" => "Restrt",
        "dead" => "Dead",
        _ => state.Length > 6 ? state[..6] : state,
    };

    private async Task<IReadOnlyList<IEntry>> ListFolderAsync(string container, string path, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            Docker,
            ["exec", container, "ls", "-lAnL", "--", ContainerPath(path)],
            cancellationToken: cancellationToken);

        // ls exits 1 when some entries could not be stat'ed (a dangling symlink under -L) and still
        // lists the rest; only an empty answer with an error is a failure.
        if (!result.Success && result.StandardOutput.Trim().Length == 0)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        return ParseListing(result.StandardOutput, container, path, DateTime.Now);
    }

    // <perms> <links> <uid> <gid> [<major>,] <size> <Mon> <day> <HH:MM|year> <name>
    [GeneratedRegex(@"^(?<perms>[-dlcbps][-rwxsStTl+.@]{9,10}\S*)\s+\d+\s+\S+\s+\S+\s+(?:\d+,\s*)?(?<size>\d+)\s+(?<month>[A-Za-z]{3})\s+(?<day>\d{1,2})\s+(?<when>\d{1,2}:\d{2}|\d{4})\s(?<name>.+)$")]
    private static partial Regex LsLine();

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>
    /// Reads <c>ls -lAnL</c>. Lines that do not have the long-listing shape (the <c>total</c> line, a
    /// locale with other month names) are skipped rather than guessed at.
    /// </summary>
    internal static List<IEntry> ParseListing(string output, string container, string path, DateTime now)
    {
        List<IEntry> folders = [];
        List<IEntry> files = [];
        var folderUrl = Container.DOCKER + container + "/" + (path.Length > 0 ? path + "/" : string.Empty);

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = LsLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            var isFolder = match.Groups["perms"].Value[0] == 'd';
            if (match.Groups["perms"].Value[0] == 'l')
            {
                // Only a link -L could not follow is still shown as one; drop the target.
                var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow > 0)
                {
                    name = name[..arrow];
                }
            }

            if (name is "." or "..")
            {
                continue;
            }

            long.TryParse(match.Groups["size"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            var modified = ParseDate(match.Groups["month"].Value, match.Groups["day"].Value, match.Groups["when"].Value, now);
            var url = new Url(folderUrl + name + (isFolder ? "/" : string.Empty));
            var entry = new VirtualDriveEntry(url, name, isFolder ? Url.Folder : size.ToFileSize(), modified) { Size = size };
            (isFolder ? folders : files).Add(entry);
        }

        folders.Sort((a, b) => StringComparer.Ordinal.Compare(a.DisplayName, b.DisplayName));
        files.Sort((a, b) => StringComparer.Ordinal.Compare(a.DisplayName, b.DisplayName));
        return [.. folders, .. files];
    }

    /// <summary>
    /// <c>ls</c> prints a time for anything from the last six months and a year otherwise; a time
    /// therefore means this year, unless that would put the date in the future.
    /// </summary>
    private static DateTime? ParseDate(string month, string day, string when, DateTime now)
    {
        var monthIndex = Array.IndexOf(Months, month);
        if (monthIndex < 0 || !int.TryParse(day, out var dayOfMonth))
        {
            return null;
        }

        try
        {
            if (when.Contains(':'))
            {
                var parts = when.Split(':');
                var date = new DateTime(now.Year, monthIndex + 1, dayOfMonth, int.Parse(parts[0]), int.Parse(parts[1]), 0);
                return date > now.AddDays(1) ? date.AddYears(-1) : date;
            }

            return new DateTime(int.Parse(when), monthIndex + 1, dayOfMonth);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public IEnumerable<Url> ListChildren(Url folderUrl)
    {
        foreach (var entry in ListAsync(folderUrl, CancellationToken.None).GetAwaiter().GetResult())
        {
            yield return entry.Url;
        }
    }

    #endregion

    #region Reading and writing files

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (container, path) = RequireInsideContainer(url);

        // docker cp writes a tar of the path to stdout, and it is read as it arrives, through a
        // pipe, rather than spooled first. The difference is a reader that stops early: the quick
        // view takes the first 64 KB of a file and then refuses more, and with a spool that would
        // still have copied a multi-gigabyte log out of the container before the first byte was
        // looked at. Stopping here cancels the producer, which kills docker cp.
        var pipe = new Pipe();
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(async () =>
        {
            try
            {
                var output = await processRunner.RunToStreamAsync(
                    Docker,
                    ["cp", $"{container}:{ContainerPath(path)}", "-"],
                    pipe.Writer.AsStream(leaveOpen: true),
                    cancellationToken: producerCancellation.Token);
                await pipe.Writer.CompleteAsync();
                return output;
            }
            catch (Exception exception)
            {
                await pipe.Writer.CompleteAsync(exception);
                throw;
            }
        });

        var found = false;
        Exception? readerFailure = null;
        try
        {
            await using var reader = new TarReader(pipe.Reader.AsStream(leaveOpen: true));
            while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
            {
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.DataStream is not null)
                {
                    await entry.DataStream.CopyToAsync(new ProgressStream(destination, progress), cancellationToken);
                    found = true;
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            // The destination refused more, the caller cancelled, or the tar was cut short.
            readerFailure = exception;
        }

        // Done reading, early or not: a docker cp still writing is stopped rather than drained.
        if (!producer.IsCompleted)
        {
            producerCancellation.Cancel();
        }

        await pipe.Reader.CompleteAsync();
        ProcessOutput? result = null;
        Exception? producerFailure = null;
        try
        {
            result = await producer;
        }
        catch (Exception exception)
        {
            producerFailure = exception;
        }

        if (found)
        {
            return;
        }

        // The tool's own error explains an unreadable tar better than the tar reader can.
        if (result is { Success: false })
        {
            throw new IOException(DescribeFailure(result));
        }

        if (readerFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(readerFailure).Throw();
        }

        if (producerFailure is not null)
        {
            throw new IOException(producerFailure.Message, producerFailure);
        }

        throw new IOException($"{url.Name} is not a regular file.");
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var buffer = new MemoryStream();
        await StreamToAsync(url, buffer, progress: null, CancellationToken.None);
        return new MemoryFile(url.Name, buffer.ToArray());
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (container, path) = RequireContainerPath(url);
        var slash = path.LastIndexOf('/');
        var parent = slash >= 0 ? path[..slash] : string.Empty;
        var name = slash >= 0 ? path[(slash + 1)..] : path;

        // TarWriter needs the entry's length before it writes the data, so the content is spooled to
        // a seekable file first; then the tar goes to `docker cp - container:folder` on stdin.
        await using var content = TemporaryFile();
        await source.CopyToAsync(new ProgressStream(content, progress), cancellationToken);
        content.Position = 0;

        await using var tar = TemporaryFile();
        await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = content,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                ModificationTime = DateTimeOffset.Now,
            };
            await writer.WriteEntryAsync(entry, cancellationToken);
        }

        tar.Position = 0;
        var result = await processRunner.RunToStreamAsync(
            Docker,
            ["cp", "-", $"{container}:{ContainerPath(parent)}"],
            Stream.Null,
            writeStandardInput: stdin => tar.CopyToAsync(stdin, cancellationToken),
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new IOException(DescribeFailure(result));
        }
    }

    public async Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        inputStream.Position = 0;
        await StreamFromAsync(url, inputStream, progress: null, CancellationToken.None);
    }

    private static FileStream TemporaryFile() =>
        new(Path.Combine(Path.GetTempPath(), "iTerminal-docker-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    #endregion

    #region Operations inside a container

    public Task CreateFolderAsync(Url url)
    {
        var (container, path) = RequireContainerPath(url);
        return ExecOrThrowAsync(container, "mkdir", "-p", "--", ContainerPath(path));
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var (container, path) = RequireInsideContainer(url);
        ExecOrThrowAsync(container, "rm", "-f", "--", ContainerPath(path)).GetAwaiter().GetResult();
        return true;
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var (container, path) = RequireInsideContainer(url);
        var task = recursive
            ? ExecOrThrowAsync(container, "rm", "-rf", "--", ContainerPath(path))
            : ExecOrThrowAsync(container, "rmdir", "--", ContainerPath(path));
        task.GetAwaiter().GetResult();
        return true;
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) =>
        SameContainerAsync(sourceUrl, targetUrl, "mv", overwrite ? "-f" : "-n");

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) =>
        SameContainerAsync(sourceUrl, targetUrl, "cp", overwrite ? "-af" : "-an");

    /// <summary>
    /// A move or copy the two panels share one file system for. Between two containers the transfer
    /// pipeline streams the bytes instead (it only takes this path when source and target resolve
    /// to the same <see cref="IFileSystem"/>), so only the same-container case lands here.
    /// </summary>
    private async Task SameContainerAsync(Url sourceUrl, Url targetUrl, string tool, string flags)
    {
        var (sourceContainer, sourcePath) = RequireInsideContainer(sourceUrl);
        var (targetContainer, targetPath) = RequireInsideContainer(targetUrl);
        if (!string.Equals(sourceContainer, targetContainer, StringComparison.Ordinal))
        {
            using var buffer = new MemoryStream();
            await StreamToAsync(sourceUrl, buffer, null, CancellationToken.None);
            buffer.Position = 0;
            await StreamFromAsync(targetUrl, buffer, null, CancellationToken.None);
            if (tool == "mv")
            {
                DeleteFile(sourceUrl, moveToRecycleBin: false);
            }

            return;
        }

        await ExecOrThrowAsync(sourceContainer, tool, flags, "--", ContainerPath(sourcePath), ContainerPath(targetPath));
    }

    public bool ExistsFile(Url url) => Test(url, "-f");

    public bool ExistsFolder(Url url)
    {
        var (container, path) = Split(url);
        return container.Length == 0 || Test(url, "-d");
    }

    private bool Test(Url url, string flag)
    {
        var (container, path) = Split(url);
        if (container.Length == 0)
        {
            return false;
        }

        var result = processRunner.RunAsync(Docker, ["exec", container, "test", flag, ContainerPath(path)])
            .GetAwaiter().GetResult();
        return result.Success;
    }

    public string GetAvailableDiskSpace() => string.Empty;

    private async Task ExecOrThrowAsync(string container, params string[] command)
    {
        var result = await processRunner.RunAsync(Docker, ["exec", container, .. command]);
        if (!result.Success)
        {
            throw new IOException(DescribeFailure(result));
        }
    }

    #endregion

    #region Containers

    public async Task ControlAsync(string container, DockerContainerAction action, CancellationToken cancellationToken)
    {
        var verb = action switch
        {
            DockerContainerAction.Start => "start",
            DockerContainerAction.Stop => "stop",
            _ => "restart",
        };

        // `docker stop` waits up to ten seconds for the process to exit before killing it, well
        // inside the runner's own timeout.
        var result = await processRunner.RunAsync(Docker, [verb, container], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }
    }

    public async Task<IReadOnlyList<string>> GetLogsAsync(string container, int lines, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            Docker,
            ["logs", "--tail", lines.ToString(CultureInfo.InvariantCulture), "--timestamps", container],
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        return MergeLogs(result.StandardOutput, result.StandardError, lines);
    }

    /// <summary>
    /// <c>docker logs</c> replays the container's stdout on stdout and its stderr on stderr, so the
    /// two arrive apart and an error would be shown out of order — after every line the program wrote
    /// once it had failed. With <c>--timestamps</c> each line starts with an RFC 3339 time of fixed
    /// width, which sorts as text; the two are merged by it (stably, so lines of one stream with the
    /// same stamp keep their order), the stamp dropped, and the last <paramref name="lines"/> kept.
    /// Terminal colour codes are taken out: a log is drawn as text, and the panel would print them.
    /// </summary>
    internal static List<string> MergeLogs(string standardOutput, string standardError, int lines)
    {
        static IEnumerable<(string Stamp, string Text)> Read(string text) =>
            text.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0)
                .Select(line =>
                {
                    var space = line.IndexOf(' ');
                    var stamp = space > 0 ? line[..space] : string.Empty;
                    return stamp.Length >= 20 && char.IsDigit(stamp[0]) && stamp.Contains('T')
                        ? (stamp, line[(space + 1)..])
                        : (string.Empty, line);
                });

        var merged = Read(standardOutput).Concat(Read(standardError))
            .Select((line, index) => (line.Stamp, line.Text, Index: index))
            .OrderBy(line => line.Stamp, StringComparer.Ordinal)
            .ThenBy(line => line.Index)
            .Select(line => AnsiEscape().Replace(line.Text, string.Empty).Replace("\t", "    "))
            .ToList();
        return merged.Count > lines ? merged[^lines..] : merged;
    }

    /// <summary>CSI sequences (colours, cursor moves), OSC sequences (titles, links) and the two-character escapes.</summary>
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])")]
    private static partial Regex AnsiEscape();

    public async Task<ProcessOutput> ExecAsync(string container, string workingDirectory, string command, CancellationToken cancellationToken)
    {
        // Through the streaming runner, which has no timeout: a build or a long query inside the
        // container is a legitimate command, and the text runner kills anything past 30 seconds.
        // Stdin is given and closed at once, so a command that reads it (a bare `cat`, a prompt)
        // ends instead of waiting for input the terminal has no way to send.
        using var output = new MemoryStream();
        var result = await processRunner.RunToStreamAsync(
            Docker,
            ["exec", "-w", workingDirectory, container, "sh", "-c", command],
            output,
            writeStandardInput: _ => Task.CompletedTask,
            cancellationToken: cancellationToken);
        return result with { StandardOutput = System.Text.Encoding.UTF8.GetString(output.ToArray()) };
    }

    #endregion

    #region URLs

    /// <summary>
    /// The container and the path inside it, without leading or trailing slash.
    /// <c>docker://web/etc/nginx/</c> is <c>("web", "etc/nginx")</c>; the drive root is <c>("", "")</c>.
    /// </summary>
    public static (string Container, string Path) Split(Url url)
    {
        var raw = url.Path.Trim('/');
        var slash = raw.IndexOf('/');
        return slash < 0 ? (raw, string.Empty) : (raw[..slash], raw[(slash + 1)..]);
    }

    private static (string Container, string Path) RequireContainerPath(Url url)
    {
        var split = Split(url);
        if (split.Container.Length == 0)
        {
            throw new IOException("Choose a container first: the drive root lists containers, not files.");
        }

        return split;
    }

    /// <summary>
    /// A path <em>below</em> a container's root: what deleting, moving and reading a file need. The
    /// container's root URL is also its row on the drive root, and <c>rm -rf -- /</c> is what a
    /// delete of it would otherwise have run inside the container.
    /// </summary>
    private static (string Container, string Path) RequireInsideContainer(Url url)
    {
        var split = RequireContainerPath(url);
        if (split.Path.Length == 0)
        {
            throw new IOException($"{split.Container} is a container, not a folder: open it to work with its files.");
        }

        return split;
    }

    /// <summary>The absolute path inside the container.</summary>
    internal static string ContainerPath(string path) => "/" + path.Trim('/');

    /// <summary>
    /// The CLI's own errors, translated where the raw text would not tell a user what to do. A daemon
    /// that is not running produces a paragraph about named pipes on Windows.
    /// </summary>
    internal static string DescribeFailure(ProcessOutput result) => DescribeDockerProblem(result) ?? result.FirstErrorLine;

    /// <summary>
    /// The failures that are docker's rather than the command's — no daemon, a stopped container —
    /// in words; null for anything else. The terminal uses it to tell "your command failed" (its
    /// stderr, shown as written) from "it never ran".
    /// </summary>
    internal static string? DescribeDockerProblem(ProcessOutput result)
    {
        var text = result.StandardError + "\n" + result.StandardOutput;
        if (text.Contains("error during connect", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dockerDesktopLinuxEngine", StringComparison.OrdinalIgnoreCase))
        {
            return "Docker is not running — start Docker Desktop or the docker service.";
        }

        // The daemon's own words, so a command inside the container that prints "is not running"
        // about some service of its own is not mistaken for this.
        if (text.Contains("Error response from daemon", StringComparison.OrdinalIgnoreCase)
            && text.Contains("is not running", StringComparison.OrdinalIgnoreCase))
        {
            return "The container is stopped — start it first (Shift+F9 on its row).";
        }

        return null;
    }

    #endregion
}

/// <summary>The <c>docker://</c> drive's back-end. See <see cref="DockerDriveService"/>.</summary>
public interface IDockerDriveService : IEntryListingFileSystem
{
    /// <summary>Lists the containers (drive root) or a folder inside one, without its <c>..</c> row.</summary>
    new Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);

    /// <summary>Starts, stops or restarts <paramref name="container"/>; throws with the CLI's error put into words.</summary>
    Task ControlAsync(string container, DockerContainerAction action, CancellationToken cancellationToken);

    /// <summary>The last <paramref name="lines"/> lines of the container's log, stdout and stderr in the order they were written.</summary>
    Task<IReadOnlyList<string>> GetLogsAsync(string container, int lines, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="command"/> with <c>sh -c</c> inside the container, in
    /// <paramref name="workingDirectory"/> (an absolute path inside it), with a closed stdin.
    /// </summary>
    Task<ProcessOutput> ExecAsync(string container, string workingDirectory, string command, CancellationToken cancellationToken);
}
