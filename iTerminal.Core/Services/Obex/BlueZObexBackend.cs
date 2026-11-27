using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Obex;

/// <summary>The device answered that the name is already there (<c>org.bluez.obex.Error.AlreadyExists</c>).</summary>
internal sealed class ObexAlreadyExistsException() : IOException("Bluetooth file transfer: the name is already there.");

/// <summary>
/// OBEX file transfer on Linux through BlueZ's <c>obexd</c>, driven with <c>busctl</c> on the session
/// bus (<c>org.bluez.obex</c>, which starts on demand). The service's own calls are the ones BlueZ
/// documents: <c>Client1.CreateSession</c> with the <c>ftp</c> target, then on the session
/// <c>FileTransfer1</c> — <c>ChangeFolder</c>, <c>ListFolder</c>, <c>GetFile</c>, <c>PutFile</c>,
/// <c>CreateFolder</c>, <c>Delete</c> — and <c>Transfer1.Status</c> for a transfer that runs in the
/// background. Every operation opens its own session and removes it afterwards, so nothing stays
/// connected between two of the app's requests.
///
/// <para>A transfer is written to or read from a temporary file, because obexd moves bytes between the
/// phone and a local path, not a stream. The Bluetooth address is checked before it reaches the command
/// line. Output is parsed from <c>busctl --json=short</c>; the parsers are internal so the formats are
/// tested against recorded output.</para>
/// </summary>
public sealed class BlueZObexBackend : IObexBackend
{
    private const string Bus = "org.bluez.obex";
    private const string Manager = "/org/bluez/obex";
    private const string ClientInterface = "org.bluez.obex.Client1";
    private const string FileTransferInterface = "org.bluez.obex.FileTransfer1";
    private const string TransferInterface = "org.bluez.obex.Transfer1";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);
    private static readonly Regex AddressPattern = new("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly IExternalProcessRunner _runner;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _transferDeadline;

    public BlueZObexBackend(IExternalProcessRunner runner, TimeSpan? pollInterval = null, TimeSpan? transferDeadline = null)
    {
        _runner = runner;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(400);
        _transferDeadline = transferDeadline ?? TimeSpan.FromMinutes(15);
    }

    public string Unavailable => string.Empty;

    // --- operations ---------------------------------------------------------------------------

    public Task<IReadOnlyList<ObexItem>> ListAsync(string address, string path, CancellationToken cancellationToken) =>
        WithSessionAsync(address, Segments(path), async session =>
        {
            var json = await CallAsync(session, FileTransferInterface, "ListFolder", string.Empty, cancellationToken).ConfigureAwait(false);
            return ParseListing(json);
        }, cancellationToken);

    public async Task DownloadAsync(string address, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (folder, name) = Split(path);
        var temp = TempFile();
        try
        {
            await WithSessionAsync(address, folder, async session =>
            {
                var json = await CallAsync(session, FileTransferInterface, "GetFile", "ss", cancellationToken, temp, name).ConfigureAwait(false);
                await WaitForTransferAsync(ParseObjectPath(json), cancellationToken).ConfigureAwait(false);
                return 0;
            }, cancellationToken).ConfigureAwait(false);

            await using var received = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await received.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                progress?.Report(total);
            }
        }
        finally
        {
            DeleteTemp(temp);
        }
    }

    public async Task UploadAsync(string address, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (folder, name) = Split(path);
        var temp = TempFile();
        try
        {
            await using (var spool = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await spool.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    progress?.Report(total);
                }
            }

            await WithSessionAsync(address, folder, async session =>
            {
                var json = await CallAsync(session, FileTransferInterface, "PutFile", "ss", cancellationToken, temp, name).ConfigureAwait(false);
                await WaitForTransferAsync(ParseObjectPath(json), cancellationToken).ConfigureAwait(false);
                return 0;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteTemp(temp);
        }
    }

    public async Task CreateFolderAsync(string address, string path, CancellationToken cancellationToken)
    {
        var (folder, name) = Split(path);
        await WithSessionAsync(address, folder, async session =>
        {
            try
            {
                await CallAsync(session, FileTransferInterface, "CreateFolder", "s", cancellationToken, name).ConfigureAwait(false);
            }
            catch (ObexAlreadyExistsException)
            {
                // Creating what is there is not an error, as on the other remote drives.
            }

            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string address, string path, bool isFolder, CancellationToken cancellationToken)
    {
        var (folder, name) = Split(path);
        await WithSessionAsync(address, folder, async session =>
        {
            await CallAsync(session, FileTransferInterface, "Delete", "s", cancellationToken, name).ConfigureAwait(false);
            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }

    // --- sessions and calls -------------------------------------------------------------------

    /// <summary>Opens a session to <paramref name="address"/>, walks to <paramref name="folder"/>, runs <paramref name="action"/> and removes the session.</summary>
    private async Task<T> WithSessionAsync<T>(string address, IReadOnlyList<string> folder, Func<string, Task<T>> action, CancellationToken cancellationToken)
    {
        ValidateAddress(address);
        var session = ParseObjectPath(await CallManagerAsync(
            ["call", Bus, Manager, ClientInterface, "CreateSession", "sa{sv}", address, "1", "Target", "s", "ftp"], cancellationToken).ConfigureAwait(false));
        try
        {
            foreach (var segment in folder)
            {
                await CallAsync(session, FileTransferInterface, "ChangeFolder", "s", cancellationToken, segment).ConfigureAwait(false);
            }

            return await action(session).ConfigureAwait(false);
        }
        finally
        {
            await CallManagerAsync(["call", Bus, Manager, ClientInterface, "RemoveSession", "o", session], CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<string> CallAsync(string session, string @interface, string method, string signature, CancellationToken cancellationToken, params string[] values)
    {
        var arguments = new List<string> { "call", Bus, session, @interface, method };
        if (signature.Length > 0)
        {
            // A name from the phone that starts with '-' would be read by busctl as an option: refused instead.
            if (values.Any(v => v.StartsWith('-')))
            {
                throw new IOException("Bluetooth file transfer: a name that starts with '-' cannot be sent to the device.");
            }

            arguments.Add(signature);
            arguments.AddRange(values);
        }

        return await CallManagerAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one <c>busctl</c> command; its JSON output, or an error in words.</summary>
    private async Task<string> CallManagerAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallTimeout);
        var output = await _runner.RunAsync("busctl", ["--user", "--json=short", .. arguments], null, timeout.Token).ConfigureAwait(false);
        if (output.ExitCode == -1)
        {
            throw new IOException("Bluetooth file transfer needs busctl (systemd), which could not be started.");
        }

        if (!output.Success)
        {
            // Read from the raw D-Bus error name: Explain() reduces it to words that no longer carry the name.
            if (output.FirstErrorLine.Contains("org.bluez.obex.Error.AlreadyExists", StringComparison.Ordinal))
            {
                throw new ObexAlreadyExistsException();
            }

            throw new IOException("Bluetooth file transfer: " + Explain(output.FirstErrorLine) + ".");
        }

        return output.StandardOutput;
    }

    /// <summary>Waits for a background transfer: <c>complete</c> returns, <c>error</c> throws, anything else is polled again.</summary>
    private async Task WaitForTransferAsync(string transfer, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _transferDeadline;
        while (true)
        {
            var status = ParseString(await CallManagerAsync(["get-property", Bus, transfer, TransferInterface, "Status"], cancellationToken).ConfigureAwait(false));
            switch (status)
            {
                case "complete":
                    return;
                case "error":
                    throw new IOException("Bluetooth file transfer: the device stopped the transfer.");
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new IOException("Bluetooth file transfer: the transfer did not finish in time.");
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // --- helpers ------------------------------------------------------------------------------

    private static void ValidateAddress(string address)
    {
        if (!AddressPattern.IsMatch(address))
        {
            throw new IOException($"Not a Bluetooth address: {address}.");
        }
    }

    /// <summary>What obexd's error says, in a short form: the part after the last colon of its error name.</summary>
    internal static string Explain(string line)
    {
        if (line.Contains("not provided by any .service files", StringComparison.Ordinal)
            || line.Contains("org.bluez.obex", StringComparison.Ordinal) && line.Contains("was not provided", StringComparison.Ordinal))
        {
            return "BlueZ's file transfer service (obexd) is not installed or not running";
        }

        var colon = line.LastIndexOf(": ", StringComparison.Ordinal);
        return colon >= 0 && colon + 2 < line.Length ? line[(colon + 2)..].TrimEnd('.') : line.TrimEnd('.');
    }

    private static IReadOnlyList<string> Segments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static (IReadOnlyList<string> Folder, string Name) Split(string path)
    {
        var segments = Segments(path);
        if (segments.Count == 0)
        {
            return (Array.Empty<string>(), string.Empty);
        }

        return (segments.Take(segments.Count - 1).ToList(), segments[^1]);
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "iTerminal-obex-" + Guid.NewGuid().ToString("N"));

    private static void DeleteTemp(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The temporary file is left for the system to clear; nothing the user can act on.
        }
    }

    /// <summary>The first <c>o</c> value of a busctl answer: an object path, alone or first among several results.</summary>
    internal static string ParseObjectPath(string json) => ParseTyped(json, "o");

    internal static string ParseString(string json) => ParseTyped(json, "s");

    private static string ParseTyped(string json, string type)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var value = root.GetProperty("data");
        if (value.ValueKind == JsonValueKind.Array)
        {
            value = value.EnumerateArray().First();
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("data", out var inner))
        {
            value = inner;
        }

        return value.GetString() ?? throw new IOException($"Bluetooth file transfer: the answer had no {type} value.");
    }

    /// <summary>
    /// The rows of a <c>ListFolder</c> answer: an array of dictionaries whose values are busctl variants
    /// (<c>{"type":"s","data":"x"}</c>). Name, Type ("folder" or "file"), Size and Modified are read; a row
    /// without a name, and the <c>.</c> and <c>..</c> rows, are dropped.
    /// </summary>
    internal static IReadOnlyList<ObexItem> ParseListing(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = new List<ObexItem>();
        foreach (var item in Dictionaries(document.RootElement.GetProperty("data")))
        {
            var name = Variant(item, "Name")?.GetString();
            if (string.IsNullOrEmpty(name) || name is "." or "..")
            {
                continue;
            }

            var isFolder = string.Equals(Variant(item, "Type")?.GetString(), "folder", StringComparison.OrdinalIgnoreCase);
            var size = Variant(item, "Size") is { } sizeValue && sizeValue.TryGetInt64(out var parsed) ? parsed : 0;
            rows.Add(new ObexItem(name, isFolder, size, ParseModified(Variant(item, "Modified")?.GetString())));
        }

        return rows;
    }

    /// <summary>Every object in an array, however deeply the array is nested: busctl wraps an <c>aa{sv}</c> in one more array.</summary>
    private static IEnumerable<JsonElement> Dictionaries(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var dictionary in Dictionaries(child))
                {
                    yield return dictionary;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    private static JsonElement? Variant(JsonElement dictionary, string key) =>
        dictionary.TryGetProperty(key, out var variant) && variant.TryGetProperty("data", out var data) ? data : null;

    /// <summary>obexd's <c>Modified</c> is <c>yyyyMMddTHHmmss</c>; an ISO date is accepted too.</summary>
    internal static DateTime? ParseModified(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime()
            : null;
    }
}
