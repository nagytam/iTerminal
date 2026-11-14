using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using iTerminal.Core.Services.PackageManager;
using iTerminal.Core.Services.RuntimeDependencies;
using iConsole.Localization;

namespace iTerminal.Core.Services.YouTube;

/// <summary>
/// YouTube through <c>yt-dlp</c>, which follows YouTube's changes so this code does not have to. It answers two questions:
/// what a search finds (<c>ytsearchN:</c>, one JSON object per line) and which address plays a video. No API key is needed.
///
/// <para>The program runs on a background task, never on a key handler. Arguments are passed as a list, never as a shell line,
/// and a video id is checked before it reaches the command line, so nothing typed can become an option.</para>
/// </summary>
public sealed class YtDlpYouTubeService : IYouTubeService
{
    /// <summary>What the search and the playback runner call; the runtime dependency puts it on the PATH.</summary>
    internal const string Executable = "yt-dlp";

    /// <summary>
    /// A single file that already has both picture and sound, at most 720 lines tall. Asking for separate streams would need
    /// ffmpeg to merge them; a combined one plays with the viewer as it is.
    /// </summary>
    internal const string PlaybackFormat = "b[height<=720]/b";

    internal const int MaximumResults = 50;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(60);
    private static readonly Regex VideoIdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant);

    /// <summary>Runs yt-dlp with the given arguments. Replaced by a fake in the tests.</summary>
    internal delegate Task<YtDlpResult> YtDlpRunner(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);

    internal readonly record struct YtDlpResult(bool Success, string StandardOutput, string StandardError);

    private readonly YtDlpRunner _run;
    private readonly Func<string, string?> _findOnPath;

    public YtDlpYouTubeService()
        : this(RunProgramAsync, GitRuntimeDependency.FindOnPath)
    {
    }

    internal YtDlpYouTubeService(YtDlpRunner run, Func<string, string?> findOnPath)
    {
        _run = run;
        _findOnPath = findOnPath;
    }

    public async Task<IReadOnlyList<YouTubeVideo>> SearchAsync(string query, int count, CancellationToken cancellationToken = default)
    {
        string term = query.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        int limit = Math.Clamp(count, 1, MaximumResults);
        YtDlpResult result = await RunAsync(
            ["--flat-playlist", "--dump-json", "--no-warnings", "--skip-download", "--", $"ytsearch{limit}:{term}"],
            SearchTimeout,
            cancellationToken);
        if (!result.Success)
        {
            throw new YouTubeException(string.Format(UiText.Get("YouTube.SearchFailed", "The search failed: {0}"), FailureMessage(result.StandardError)));
        }

        return ParseSearchOutput(result.StandardOutput);
    }

    public async Task<string> ResolvePlaybackUrlAsync(string videoId, CancellationToken cancellationToken = default)
    {
        if (!IsVideoId(videoId))
        {
            throw new YouTubeException(UiText.Get("YouTube.BadId", "That is not a YouTube video."));
        }

        YtDlpResult result = await RunAsync(
            ["--no-playlist", "--no-warnings", "-g", "-f", PlaybackFormat, "--", $"https://www.youtube.com/watch?v={videoId}"],
            ResolveTimeout,
            cancellationToken);
        if (!result.Success)
        {
            throw new YouTubeException(string.Format(UiText.Get("YouTube.PlayFailed", "The video could not be played: {0}"), FailureMessage(result.StandardError)));
        }

        return FirstPlayableUrl(result.StandardOutput)
            ?? throw new YouTubeException(UiText.Get("YouTube.NoStream", "YouTube gave no stream for this video."));
    }

    private async Task<YtDlpResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_findOnPath(Executable) is null)
        {
            throw new YouTubeException(UiText.Get("YouTube.Missing", "YouTube needs yt-dlp. Tools > Runtime Dependencies installs it."));
        }

        YtDlpResult result = await _run(arguments, timeout, cancellationToken);

        // A cancelled run reports as a failed one from the process helper; the caller must see the cancellation instead.
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task<YtDlpResult> RunProgramAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        PackageManagerProcess.ProcessResult result = await PackageManagerProcess.RunCaptureAsync(Executable, timeout, cancellationToken, arguments.ToArray());
        return new YtDlpResult(result.Success, result.StandardOutput, result.StandardError);
    }

    internal static bool IsVideoId(string? id) => id is not null && VideoIdPattern.IsMatch(id);

    /// <summary>One <see cref="YouTubeVideo"/> per JSON line that has an id and a title; anything else is skipped, not fatal.</summary>
    internal static IReadOnlyList<YouTubeVideo> ParseSearchOutput(string output)
    {
        List<YouTubeVideo> videos = [];
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || StringOf(root, "id") is not { } id || !IsVideoId(id)
                    || StringOf(root, "title") is not { } title)
                {
                    continue;
                }

                string channel = StringOf(root, "channel") ?? StringOf(root, "uploader") ?? string.Empty;
                TimeSpan? duration = NumberOf(root, "duration") is { } seconds and >= 0 ? TimeSpan.FromSeconds(seconds) : null;
                long? views = NumberOf(root, "view_count") is { } count and >= 0 ? (long)count : null;
                DateTime? published = DateOf(root, "upload_date");
                videos.Add(new YouTubeVideo(id, title, channel, duration, views, published));
            }
            catch (JsonException)
            {
                // A line that is not JSON (a warning that slipped past --no-warnings) is not a video.
            }
        }

        return videos;
    }

    /// <summary>The first line that is an address. With <c>-g</c> yt-dlp prints one per stream it chose.</summary>
    internal static string? FirstPlayableUrl(string output)
    {
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>yt-dlp's own "ERROR:" line when there is one, otherwise the first line it wrote.</summary>
    internal static string FailureMessage(string stderr)
    {
        string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? error = lines.LastOrDefault(line => line.StartsWith("ERROR:", StringComparison.Ordinal));
        string message = error ?? lines.FirstOrDefault() ?? UiText.Get("YouTube.UnknownError", "yt-dlp stopped without a message.");
        return message.StartsWith("ERROR:", StringComparison.Ordinal) ? message["ERROR:".Length..].Trim() : message;
    }

    private static string? StringOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static double? NumberOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
            ? number
            : null;

    private static DateTime? DateOf(JsonElement root, string name) =>
        StringOf(root, name) is { } text && DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date
            : null;
}
