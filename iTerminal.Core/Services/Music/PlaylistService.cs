using iTerminal.Core.Audio;
using iTerminal.Core.Data.Music;
using System.Globalization;
using System.Text;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// M3U-backed playlist storage. Writing always produces UTF-8 extended M3U (<c>#EXTM3U</c> with
/// <c>#EXTINF</c> lines) so other players show the same titles; reading tolerates the plain
/// one-path-per-line form as well, since that is what many tools emit.
/// </summary>
public sealed class PlaylistService : IPlaylistService
{
    private const string LastPlaylistFileName = "last.m3u8";
    private const int MaxFolderScanDepth = 8;

    private readonly string _playlistDirectory;

    public PlaylistService(string? playlistDirectory = null)
    {
        _playlistDirectory = playlistDirectory ?? Path.Combine(
            iTerminal.Core.Application.iTerminalApplicationBuilderExtension.UserDataRoot(),
            "iTerminal",
            "playlists");
    }

    public IReadOnlySet<string> PlaylistExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".m3u", ".m3u8" };

    public string LastPlaylistPath => Path.Combine(_playlistDirectory, LastPlaylistFileName);

    public bool IsPlaylistFile(string path) =>
        !string.IsNullOrEmpty(path) && PlaylistExtensions.Contains(Path.GetExtension(path));

    public IReadOnlyList<MusicTrack> CreateTracks(string path, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        try
        {
            if (Directory.Exists(path))
            {
                return ScanFolder(path, recursive);
            }

            if (!File.Exists(path))
            {
                return [];
            }

            if (IsPlaylistFile(path))
            {
                return Load(path);
            }

            return AudioDecoderFactory.IsAudioFile(path)
                ? [MusicTagReader.CreateTrack(path)]
                : [];
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<MusicTrack> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            var tracks = new List<MusicTrack>();

            // Pending #EXTINF metadata applies to the next path line.
            var pendingTitle = string.Empty;
            var pendingArtist = string.Empty;
            var pendingDuration = TimeSpan.Zero;

            foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith('#'))
                {
                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    {
                        (pendingDuration, pendingArtist, pendingTitle) = ParseExtInf(line);
                    }

                    continue;
                }

                var resolved = ResolvePath(line, baseDirectory);
                if (resolved is null)
                {
                    continue;
                }

                tracks.Add(new MusicTrack(resolved)
                {
                    Title = pendingTitle,
                    Artist = pendingArtist,
                    Duration = pendingDuration,
                });

                pendingTitle = string.Empty;
                pendingArtist = string.Empty;
                pendingDuration = TimeSpan.Zero;
            }

            return tracks;
        }
        catch
        {
            return [];
        }
    }

    public void Save(string path, IEnumerable<MusicTrack> tracks)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");

        foreach (var track in tracks ?? [])
        {
            if (track is null)
            {
                continue;
            }

            var seconds = track.Duration > TimeSpan.Zero
                ? (int)Math.Round(track.Duration.TotalSeconds)
                : -1;
            var label = string.IsNullOrWhiteSpace(track.Artist)
                ? track.DisplayTitle
                : $"{track.Artist} - {track.DisplayTitle}";

            builder.Append("#EXTINF:")
                .Append(seconds.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .AppendLine(label);
            builder.AppendLine(track.Path);
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Reads the queue file written by versions that kept the queue on disk. The database is where
    /// the queue lives now, so this is only ever read, and only when that table is empty.
    /// </summary>
    public IReadOnlyList<MusicTrack> LoadLast() => Load(LastPlaylistPath);

    private IReadOnlyList<MusicTrack> ScanFolder(string folder, bool recursive)
    {
        var files = new List<string>();
        CollectAudioFiles(folder, recursive, MaxFolderScanDepth, files);

        // Natural-ish ordering so "track 2" sorts before "track 10" the way the folder shows them.
        files.Sort((left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));

        var tracks = new List<MusicTrack>(files.Count);
        foreach (var file in files)
        {
            tracks.Add(MusicTagReader.CreateTrack(file));
        }

        return tracks;
    }

    private static void CollectAudioFiles(string folder, bool recursive, int depthBudget, List<string> files)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (AudioDecoderFactory.IsAudioFile(file))
                {
                    files.Add(file);
                }
            }

            if (!recursive || depthBudget <= 0)
            {
                return;
            }

            foreach (var subFolder in Directory.EnumerateDirectories(folder))
            {
                CollectAudioFiles(subFolder, recursive, depthBudget - 1, files);
            }
        }
        catch
        {
            // Unreadable folders are skipped rather than aborting the whole scan.
        }
    }

    internal static (TimeSpan Duration, string Artist, string Title) ParseExtInf(string line)
    {
        // #EXTINF:<seconds>,<artist> - <title>
        var payload = line["#EXTINF:".Length..];
        var comma = payload.IndexOf(',');
        if (comma < 0)
        {
            return (TimeSpan.Zero, string.Empty, string.Empty);
        }

        var duration = TimeSpan.Zero;
        if (double.TryParse(payload[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            duration = TimeSpan.FromSeconds(seconds);
        }

        var label = payload[(comma + 1)..].Trim();
        var separator = label.IndexOf(" - ", StringComparison.Ordinal);
        return separator > 0
            ? (duration, label[..separator].Trim(), label[(separator + 3)..].Trim())
            : (duration, string.Empty, label);
    }

    private static string? ResolvePath(string entry, string baseDirectory)
    {
        try
        {
            // Remote entries are meaningless to a local decoder, so they are dropped on load.
            if (entry.Contains("://", StringComparison.Ordinal))
            {
                return null;
            }

            var path = Path.IsPathRooted(entry)
                ? entry
                : Path.GetFullPath(Path.Combine(baseDirectory, entry));

            return path;
        }
        catch
        {
            return null;
        }
    }
}
