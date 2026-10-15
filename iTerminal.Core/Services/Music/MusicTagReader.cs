using iTerminal.Core.Data.Music;
using System.Buffers.Binary;
using System.Text;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// ID3 reader. Two callers with different appetites share one parser: the playlist and the library
/// index want three strings (<see cref="Read"/>), while the music file viewer lists everything the
/// tag block carries (<see cref="ReadAll"/>). Frames are read once and both shapes come off the same
/// pass, so a file is never parsed twice.
///
/// <para>Handles ID3v2.2/2.3/2.4 at the head of the file and ID3v1 in the last 128 bytes. Binary
/// frames are skipped except cover art, which is reported by size — a terminal cannot show the
/// image, but "there is one, 84 KB" is the useful part. Never throws: unreadable or absent tags
/// yield empty results.</para>
/// </summary>
public static class MusicTagReader
{
    private const int MaxTagBytes = 2 * 1024 * 1024;

    /// <summary>Title, artist and album for a file. All fields empty when nothing could be read.</summary>
    public readonly record struct MusicTags(string Title, string Artist, string Album)
    {
        public static readonly MusicTags Empty = new(string.Empty, string.Empty, string.Empty);

        public bool IsEmpty =>
            string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Artist) && string.IsNullOrEmpty(Album);
    }

    /// <summary>
    /// Display names for the frames worth spelling out. Both the 4-character ids of ID3v2.3/2.4 and
    /// the 3-character ones of ID3v2.2 map here; anything absent falls back to its own frame id, so
    /// an exotic tag is still shown rather than silently dropped.
    /// </summary>
    private static readonly Dictionary<string, string> FrameNames = new(StringComparer.Ordinal)
    {
        ["TIT2"] = "Title", ["TT2"] = "Title",
        ["TPE1"] = "Artist", ["TP1"] = "Artist",
        ["TALB"] = "Album", ["TAL"] = "Album",
        ["TPE2"] = "Album artist", ["TP2"] = "Album artist",
        ["TPE3"] = "Conductor", ["TP3"] = "Conductor",
        ["TPE4"] = "Remixed by", ["TP4"] = "Remixed by",
        ["TCOM"] = "Composer", ["TCM"] = "Composer",
        ["TEXT"] = "Lyricist", ["TXT"] = "Lyricist",
        ["TCON"] = "Genre", ["TCO"] = "Genre",
        ["TYER"] = "Year", ["TYE"] = "Year",
        ["TDRC"] = "Recorded", ["TDRL"] = "Released", ["TDAT"] = "Date", ["TDA"] = "Date",
        ["TRCK"] = "Track", ["TRK"] = "Track",
        ["TPOS"] = "Disc", ["TPA"] = "Disc",
        ["TBPM"] = "BPM", ["TBP"] = "BPM",
        ["TLEN"] = "Length", ["TLE"] = "Length",
        ["TPUB"] = "Publisher", ["TPB"] = "Publisher",
        ["TCOP"] = "Copyright", ["TCR"] = "Copyright",
        ["TENC"] = "Encoded by", ["TEN"] = "Encoded by",
        ["TSSE"] = "Encoder settings", ["TSS"] = "Encoder settings",
        ["TSRC"] = "ISRC", ["TRC"] = "ISRC",
        ["TCMP"] = "Compilation",
        ["TOPE"] = "Original artist", ["TOA"] = "Original artist",
        ["TOAL"] = "Original album", ["TOT"] = "Original album",
        ["TIT1"] = "Grouping", ["TT1"] = "Grouping",
        ["TIT3"] = "Subtitle", ["TT3"] = "Subtitle",
        ["TKEY"] = "Key", ["TKE"] = "Key",
        ["TLAN"] = "Language", ["TLA"] = "Language",
        ["TMOO"] = "Mood",
        ["TMED"] = "Media type", ["TMT"] = "Media type",
        ["TSOA"] = "Album sort", ["TSOP"] = "Artist sort", ["TSOT"] = "Title sort",
        ["TOWN"] = "Owner",
        ["WOAR"] = "Artist URL", ["WAR"] = "Artist URL",
        ["WOAF"] = "File URL", ["WAF"] = "File URL",
        ["WPUB"] = "Publisher URL", ["WPB"] = "Publisher URL",
        ["COMM"] = "Comment", ["COM"] = "Comment",
        ["APIC"] = "Cover art", ["PIC"] = "Cover art",
    };

    /// <summary>
    /// The original ID3v1 genre list. Numeric genres survive into ID3v2 as <c>TCON</c> values like
    /// "(17)", so both readers resolve them through here; anything past the end stays numeric.
    /// </summary>
    private static readonly string[] Id3V1Genres =
    [
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop",
        "Jazz", "Metal", "New Age", "Oldies", "Other", "Pop", "R&B", "Rap",
        "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska", "Death Metal", "Pranks",
        "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion", "Trance",
        "Classical", "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise",
        "Alternative Rock", "Bass", "Soul", "Punk", "Space", "Meditative", "Instrumental Pop", "Instrumental Rock",
        "Ethnic", "Gothic", "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream",
        "Southern Rock", "Comedy", "Cult", "Gangsta", "Top 40", "Christian Rap", "Pop/Funk", "Jungle",
        "Native US", "Cabaret", "New Wave", "Psychedelic", "Rave", "Showtunes", "Trailer", "Lo-Fi",
        "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll", "Hard Rock",
    ];

    /// <summary>Title, artist and album only — what the playlist and the library index need.</summary>
    public static MusicTags Read(string path)
    {
        MusicTagSet set = ReadAll(path);
        return new MusicTags(set.Title, set.Artist, set.Album);
    }

    /// <summary>
    /// Every tag in the file. ID3v2 wins when present; a file carrying only the 128-byte ID3v1
    /// trailer falls back to that.
    /// </summary>
    public static MusicTagSet ReadAll(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            MusicTagSet tags = ReadId3V2(stream);
            if (!tags.IsEmpty)
            {
                return tags;
            }

            return ReadId3V1(stream);
        }
        catch
        {
            return MusicTagSet.Empty;
        }
    }

    /// <summary>Builds a track for a file, filling in whatever tags it carries.</summary>
    public static MusicTrack CreateTrack(string path)
    {
        MusicTags tags = Read(path);
        return new MusicTrack(path)
        {
            Title = tags.Title,
            Artist = tags.Artist,
            Album = tags.Album,
        };
    }

    private static MusicTagSet ReadId3V2(Stream stream)
    {
        stream.Position = 0;

        Span<byte> header = stackalloc byte[10];
        if (ReadExactly(stream, header) != header.Length || !header[..3].SequenceEqual("ID3"u8))
        {
            return MusicTagSet.Empty;
        }

        var majorVersion = header[3];
        var headerFlags = header[5];
        var tagSize = ReadSynchSafe(header[6..10]);
        if (tagSize <= 0 || tagSize > MaxTagBytes)
        {
            return MusicTagSet.Empty;
        }

        var body = new byte[tagSize];
        if (ReadExactly(stream, body) != tagSize)
        {
            return MusicTagSet.Empty;
        }

        // v2.2 used 3-character frame ids and 3-byte sizes; 2.3 and 2.4 both use 4 and 4.
        var idLength = majorVersion <= 2 ? 3 : 4;
        var headerLength = majorVersion <= 2 ? 6 : 10;

        List<MusicTag> tags = [];
        string title = string.Empty, artist = string.Empty, album = string.Empty;
        var offset = SkipExtendedHeader(body, majorVersion, headerFlags);

        while (offset + headerLength <= body.Length)
        {
            var frameId = Encoding.ASCII.GetString(body, offset, idLength);
            if (frameId[0] == '\0')
            {
                break; // padding
            }

            int frameSize;
            if (majorVersion <= 2)
            {
                frameSize = (body[offset + 3] << 16) | (body[offset + 4] << 8) | body[offset + 5];
            }
            else if (majorVersion >= 4)
            {
                frameSize = ReadSynchSafe(body.AsSpan(offset + 4, 4));
            }
            else
            {
                frameSize = (int)BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(offset + 4, 4));
            }

            var frameStart = offset + headerLength;
            if (frameSize <= 0 || frameStart + frameSize > body.Length)
            {
                break;
            }

            var frame = body.AsSpan(frameStart, frameSize);
            switch (frameId)
            {
                case "TIT2" or "TT2":
                    title = DecodeTextFrame(frame);
                    break;
                case "TPE1" or "TP1":
                    artist = DecodeTextFrame(frame);
                    break;
                case "TALB" or "TAL":
                    album = DecodeTextFrame(frame);
                    break;
            }

            AddFrameTag(tags, frameId, frame);

            offset = frameStart + frameSize;
        }

        if (tags.Count == 0)
        {
            return MusicTagSet.Empty;
        }

        return new MusicTagSet
        {
            Source = $"ID3v2.{majorVersion}",
            Title = title,
            Artist = artist,
            Album = album,
            Tags = tags,
        };
    }

    /// <summary>
    /// Returns the offset of the first frame, stepping over the optional extended header. Without
    /// this, a tag written with one (foobar2000 and iTunes both do) would parse its first "frame"
    /// out of the extended header's bytes and abort the whole tag.
    /// </summary>
    private static int SkipExtendedHeader(byte[] body, byte majorVersion, byte headerFlags)
    {
        if ((headerFlags & 0x40) == 0 || majorVersion < 3 || body.Length < 4)
        {
            return 0;
        }

        // v2.3 states the size of what follows the size field; v2.4 states a synch-safe size that
        // includes it.
        var extendedSize = majorVersion >= 4
            ? ReadSynchSafe(body.AsSpan(0, 4))
            : (int)BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(0, 4)) + 4;

        return extendedSize > 0 && extendedSize < body.Length ? extendedSize : 0;
    }

    private static void AddFrameTag(List<MusicTag> tags, string frameId, ReadOnlySpan<byte> frame)
    {
        string value;
        var name = FrameNames.TryGetValue(frameId, out var friendly) ? friendly : frameId;

        switch (frameId)
        {
            case "COMM" or "COM":
                value = DecodeCommentFrame(frame);
                break;

            case "APIC" or "PIC":
                value = $"{frame.Length:N0} bytes";
                break;

            case "TXXX" or "TXX":
                (name, value) = DecodeUserTextFrame(frame);
                break;

            case "TCON" or "TCO":
                value = ResolveGenre(DecodeTextFrame(frame));
                break;

            default:
                if (frameId[0] == 'T')
                {
                    value = DecodeTextFrame(frame);
                }
                else if (frameId[0] == 'W')
                {
                    value = Sanitize(Encoding.Latin1.GetString(frame));
                }
                else
                {
                    // Binary frames (private data, popularimeter, ...) have nothing to show.
                    return;
                }

                break;
        }

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
        {
            tags.Add(new MusicTag(name, value));
        }
    }

    private static MusicTagSet ReadId3V1(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 128)
        {
            return MusicTagSet.Empty;
        }

        stream.Position = stream.Length - 128;
        var tag = new byte[128];
        if (ReadExactly(stream, tag) != tag.Length || !tag.AsSpan(0, 3).SequenceEqual("TAG"u8))
        {
            return MusicTagSet.Empty;
        }

        var title = DecodeFixed(tag.AsSpan(3, 30));
        var artist = DecodeFixed(tag.AsSpan(33, 30));
        var album = DecodeFixed(tag.AsSpan(63, 30));
        var year = DecodeFixed(tag.AsSpan(93, 4));

        // ID3v1.1 stole the last two comment bytes for a track number: a zero at [125] marks it.
        var hasTrackNumber = tag[125] == 0 && tag[126] != 0;
        var comment = DecodeFixed(hasTrackNumber ? tag.AsSpan(97, 28) : tag.AsSpan(97, 30));
        var track = hasTrackNumber ? tag[126].ToString() : string.Empty;
        var genre = tag[127] < Id3V1Genres.Length ? Id3V1Genres[tag[127]] : string.Empty;

        List<MusicTag> tags = [];
        AddIfPresent(tags, "Title", title);
        AddIfPresent(tags, "Artist", artist);
        AddIfPresent(tags, "Album", album);
        AddIfPresent(tags, "Year", year);
        AddIfPresent(tags, "Track", track);
        AddIfPresent(tags, "Genre", genre);
        AddIfPresent(tags, "Comment", comment);

        if (tags.Count == 0)
        {
            return MusicTagSet.Empty;
        }

        return new MusicTagSet
        {
            Source = hasTrackNumber ? "ID3v1.1" : "ID3v1",
            Title = title,
            Artist = artist,
            Album = album,
            Tags = tags,
        };
    }

    private static void AddIfPresent(List<MusicTag> tags, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags.Add(new MusicTag(name, value));
        }
    }

    /// <summary>Turns "(17)" or "17" into "Rock", and leaves a genre that is already a word alone.</summary>
    private static string ResolveGenre(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return value;
        }

        var digits = trimmed.StartsWith('(') && trimmed.EndsWith(')')
            ? trimmed[1..^1]
            : trimmed;

        if (int.TryParse(digits, out var index) && index >= 0 && index < Id3V1Genres.Length)
        {
            return Id3V1Genres[index];
        }

        return value;
    }

    private static string DecodeTextFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            return string.Empty;
        }

        return Sanitize(DecodeString(frame[0], frame[1..]));
    }

    /// <summary>
    /// A comment frame is an encoding byte, a three-letter language code, a null-terminated
    /// description and then the text. Only the text is shown — the description is nearly always
    /// empty or a tagger's own label.
    /// </summary>
    private static string DecodeCommentFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5)
        {
            return string.Empty;
        }

        var encoding = frame[0];
        var payload = frame[4..];
        var (_, text) = SplitAtTerminator(encoding, payload);
        return Sanitize(text);
    }

    /// <summary>A user-defined text frame carries its own name: description, then value.</summary>
    private static (string Name, string Value) DecodeUserTextFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            return (string.Empty, string.Empty);
        }

        var encoding = frame[0];
        var (description, value) = SplitAtTerminator(encoding, frame[1..]);
        return (Sanitize(description), Sanitize(value));
    }

    /// <summary>
    /// Splits a null-terminated description from the text that follows it. The terminator is two
    /// bytes wide in the UTF-16 encodings, which is why this cannot be a plain IndexOf(0).
    /// </summary>
    private static (string First, string Remainder) SplitAtTerminator(byte encoding, ReadOnlySpan<byte> payload)
    {
        var isWide = encoding is 1 or 2;
        var step = isWide ? 2 : 1;

        for (var i = 0; i + step <= payload.Length; i += step)
        {
            var isTerminator = isWide ? payload[i] == 0 && payload[i + 1] == 0 : payload[i] == 0;
            if (isTerminator)
            {
                return (DecodeString(encoding, payload[..i]), DecodeString(encoding, payload[(i + step)..]));
            }
        }

        return (string.Empty, DecodeString(encoding, payload));
    }

    private static string DecodeString(byte encodingByte, ReadOnlySpan<byte> payload) => encodingByte switch
    {
        1 => DecodeUtf16WithBom(payload),
        2 => Encoding.BigEndianUnicode.GetString(payload),
        3 => Encoding.UTF8.GetString(payload),
        _ => Encoding.Latin1.GetString(payload),
    };

    private static string DecodeUtf16WithBom(ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 2)
        {
            if (payload[0] == 0xFF && payload[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(payload[2..]);
            }

            if (payload[0] == 0xFE && payload[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(payload[2..]);
            }
        }

        return Encoding.Unicode.GetString(payload);
    }

    private static string DecodeFixed(ReadOnlySpan<byte> value) =>
        Sanitize(Encoding.Latin1.GetString(value));

    private static string Sanitize(string value)
    {
        var end = value.IndexOf('\0');
        if (end >= 0)
        {
            value = value[..end];
        }

        return value.Trim();
    }

    private static int ReadSynchSafe(ReadOnlySpan<byte> value) =>
        ((value[0] & 0x7F) << 21) | ((value[1] & 0x7F) << 14) | ((value[2] & 0x7F) << 7) | (value[3] & 0x7F);

    private static int ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }
}
