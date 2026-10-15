namespace iTerminal.Core.Data.Music;

/// <summary>
/// What the music file viewer knows about one file: its tags, its playing time and the technical
/// shape of the audio. Reading it touches the disk, so it is produced by
/// <c>IMusicMetadataService</c> on a background task and never built in a key handler.
///
/// <para>Every field is best-effort. A file whose format needs ffmpeg still yields tags, size and
/// name — only <see cref="Duration"/> and the format fields stay at zero, which the viewer shows as
/// "unknown" rather than pretending.</para>
/// </summary>
public sealed record MusicFileMetadata
{
    public static readonly MusicFileMetadata Empty = new();

    public string Path { get; init; } = string.Empty;

    public MusicTagSet TagSet { get; init; } = MusicTagSet.Empty;

    /// <summary>Playing time, or <see cref="TimeSpan.Zero"/> when no decoder here could state it.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>File size in bytes.</summary>
    public long Size { get; init; }

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    /// <summary>Bits per sample of the *source*, known only for formats that state it (FLAC). 0 otherwise.</summary>
    public int BitsPerSample { get; init; }

    /// <summary>Container/codec label taken from the extension, e.g. "MP3".</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>True when this machine can play the file right now (native format, or ffmpeg present).</summary>
    public bool CanPlay { get; init; }

    /// <summary>Why the file could not be read, when it could not be. Empty on success.</summary>
    public string Error { get; init; } = string.Empty;

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Tagged title, falling back to the file name without its extension.</summary>
    public string Title =>
        !string.IsNullOrWhiteSpace(TagSet.Title)
            ? TagSet.Title
            : System.IO.Path.GetFileNameWithoutExtension(Path);

    public string Artist => TagSet.Artist;

    public string Album => TagSet.Album;

    /// <summary>"MP3 · 44.1 kHz · stereo · 16 bit", with the parts that are unknown left out.</summary>
    public string FormatSummary
    {
        get
        {
            List<string> parts = [];
            if (!string.IsNullOrEmpty(Format))
            {
                parts.Add(Format);
            }

            if (SampleRate > 0)
            {
                parts.Add($"{SampleRate / 1000.0:0.#} kHz");
            }

            if (Channels > 0)
            {
                parts.Add(Channels switch
                {
                    1 => "mono",
                    2 => "stereo",
                    _ => $"{Channels} channels",
                });
            }

            if (BitsPerSample > 0)
            {
                parts.Add($"{BitsPerSample} bit");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The playlist entry for this file, so the viewer can hand it to the player as-is.</summary>
    public MusicTrack ToTrack() => new(Path)
    {
        Title = TagSet.Title,
        Artist = TagSet.Artist,
        Album = TagSet.Album,
        Duration = Duration,
    };
}
