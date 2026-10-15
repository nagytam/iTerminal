using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// Files on the Linux clipboard through <c>wl-copy</c>/<c>wl-paste</c> (Wayland) or <c>xclip</c> (X11),
/// in <c>x-special/gnome-copied-files</c> — "copy" or "cut" and then one <c>file://</c> URI per line,
/// which GNOME Files, Nemo, Caja and Thunar read; Dolphin and PCManFM read the same target. When
/// neither tool is installed the calls say so rather than pretending: the commands fall back to an
/// in-process clipboard.
/// </summary>
public sealed class LinuxFileClipboardService(IExternalProcessRunner processRunner) : IFileClipboardService
{
    private const string Target = "x-special/gnome-copied-files";

    public async Task SetFilesAsync(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken = default)
    {
        string payload = BuildPayload(paths, isCut);
        using MemoryStream sink = new();
        ProcessOutput output = await RunWithInputAsync(payload, cancellationToken, sink);
        if (!output.Success)
        {
            throw new IOException(output.FirstErrorLine);
        }
    }

    public async Task<FileClipboardContent?> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        foreach ((string program, string[] arguments) in ReadCommands())
        {
            ProcessOutput output = await processRunner.RunAsync(program, arguments, cancellationToken: cancellationToken);
            if (output.Success && TryParse(output.StandardOutput) is { } content)
            {
                return content;
            }
        }

        return null;
    }

    internal static string BuildPayload(IReadOnlyList<string> paths, bool isCut) =>
        (isCut ? "cut" : "copy") + "\n" + string.Join('\n', paths.Select(p => new Uri(p).AbsoluteUri));

    internal static FileClipboardContent? TryParse(string text)
    {
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2 || (lines[0] != "copy" && lines[0] != "cut"))
        {
            return null;
        }

        List<string> paths = [];
        foreach (string line in lines.Skip(1))
        {
            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            {
                paths.Add(uri.LocalPath);
            }
        }

        return paths.Count == 0 ? null : new FileClipboardContent(paths, lines[0] == "cut");
    }

    private static IEnumerable<(string Program, string[] Arguments)> ReadCommands()
    {
        yield return ("wl-paste", ["--no-newline", "--type", Target]);
        yield return ("xclip", ["-selection", "clipboard", "-o", "-t", Target]);
    }

    private async Task<ProcessOutput> RunWithInputAsync(string payload, CancellationToken cancellationToken, Stream sink)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        ProcessOutput wayland = await processRunner.RunToStreamAsync(
            "wl-copy", ["--type", Target], sink, writeStandardInput: s => s.WriteAsync(bytes, cancellationToken).AsTask(), cancellationToken: cancellationToken);
        if (wayland.Success)
        {
            return wayland;
        }

        return await processRunner.RunToStreamAsync(
            "xclip", ["-selection", "clipboard", "-t", Target, "-i"], sink, writeStandardInput: s => s.WriteAsync(bytes, cancellationToken).AsTask(), cancellationToken: cancellationToken);
    }
}
