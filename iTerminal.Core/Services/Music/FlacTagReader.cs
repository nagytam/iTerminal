using iTerminal.Core.Data.Music;
using System.Buffers.Binary;
using System.Text;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Reads a FLAC file's metadata blocks: the Vorbis comment (its tags) and STREAMINFO (sample rate,
/// channel count, bit depth and playing time).
///
/// <para>FLAC is decoded through ffmpeg here, so without this the viewer would have nothing to show
/// for a format whose whole selling point is lossless audio with proper tags — and probing duration
/// through ffmpeg would fork a process per file. The metadata blocks sit in plain sight at the head
/// of the file, so reading them costs one short read and no external tool.</para>
///
/// <para>Never throws: a truncated or non-FLAC file yields <see cref="FlacInfo.Empty"/>.</para>
/// </summary>
public static class FlacTagReader
{
    private const int StreamInfoBlockType = 0;
    private const int VorbisCommentBlockType = 4;

    /// <summary>Guards against a corrupt length field turning into a huge allocation.</summary>
    private const int MaxBlockBytes = 4 * 1024 * 1024;

    /// <summary>What the metadata blocks said. Fields stay at zero when the file did not state them.</summary>
    public sealed record FlacInfo(
        MusicTagSet Tags,
        TimeSpan Duration,
        int SampleRate,
        int Channels,
        int BitsPerSample)
    {
        public static readonly FlacInfo Empty = new(MusicTagSet.Empty, TimeSpan.Zero, 0, 0, 0);
    }

    /// <summary>
    /// Vorbis comment field names are free-form and conventionally upper case; these are the ones
    /// worth presenting in the app's own wording. Anything else is shown under its own name.
    /// </summary>
    private static readonly Dictionary<string, string> FieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TITLE"] = "Title",
        ["ARTIST"] = "Artist",
        ["ALBUM"] = "Album",
        ["ALBUMARTIST"] = "Album artist",
        ["ALBUM ARTIST"] = "Album artist",
        ["TRACKNUMBER"] = "Track",
        ["TRACKTOTAL"] = "Track count",
        ["TOTALTRACKS"] = "Track count",
        ["DISCNUMBER"] = "Disc",
        ["DISCTOTAL"] = "Disc count",
        ["DATE"] = "Date",
        ["YEAR"] = "Year",
        ["ORIGINALDATE"] = "Original date",
        ["GENRE"] = "Genre",
        ["COMPOSER"] = "Composer",
        ["PERFORMER"] = "Performer",
        ["CONDUCTOR"] = "Conductor",
        ["LYRICIST"] = "Lyricist",
        ["COMMENT"] = "Comment",
        ["DESCRIPTION"] = "Comment",
        ["ORGANIZATION"] = "Publisher",
        ["LABEL"] = "Publisher",
        ["COPYRIGHT"] = "Copyright",
        ["ISRC"] = "ISRC",
        ["ENCODER"] = "Encoded by",
        ["ENCODEDBY"] = "Encoded by",
        ["BPM"] = "BPM",
        ["COMPILATION"] = "Compilation",
        ["MOOD"] = "Mood",
        ["LANGUAGE"] = "Language",
    };

    public static bool CanRead(string path) =>
        !string.IsNullOrEmpty(path)
        && Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase);

    public static FlacInfo Read(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            Span<byte> magic = stackalloc byte[4];
            if (ReadExactly(stream, magic) != 4 || !magic.SequenceEqual("fLaC"u8))
            {
                return FlacInfo.Empty;
            }

            MusicTagSet tags = MusicTagSet.Empty;
            var sampleRate = 0;
            var channels = 0;
            var bitsPerSample = 0;
            var duration = TimeSpan.Zero;

            Span<byte> blockHeader = stackalloc byte[4];
            while (ReadExactly(stream, blockHeader) == 4)
            {
                var isLast = (blockHeader[0] & 0x80) != 0;
                var blockType = blockHeader[0] & 0x7F;
                var blockLength = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];

                if (blockLength < 0 || blockLength > MaxBlockBytes)
                {
                    break;
                }

                if (blockType == StreamInfoBlockType && blockLength >= 34)
                {
                    var block = new byte[blockLength];
                    if (ReadExactly(stream, block) != blockLength)
                    {
                        break;
                    }

                    (sampleRate, channels, bitsPerSample, duration) = ParseStreamInfo(block);
                }
                else if (blockType == VorbisCommentBlockType)
                {
                    var block = new byte[blockLength];
                    if (ReadExactly(stream, block) != blockLength)
                    {
                        break;
                    }

                    tags = ParseVorbisComment(block);
                }
                else
                {
                    // Padding, seek tables and pictures are of no interest; step over them.
                    stream.Seek(blockLength, SeekOrigin.Current);
                }

                if (isLast)
                {
                    break;
                }
            }

            return new FlacInfo(tags, duration, sampleRate, channels, bitsPerSample);
        }
        catch
        {
            return FlacInfo.Empty;
        }
    }

    /// <summary>
    /// STREAMINFO packs the interesting fields into bits rather than bytes: after the block/frame
    /// sizes come 20 bits of sample rate, 3 of channel count, 5 of bit depth and 36 of total sample
    /// count — 64 bits in all, which is why this reads one big-endian ulong at offset 10.
    /// </summary>
    private static (int SampleRate, int Channels, int BitsPerSample, TimeSpan Duration) ParseStreamInfo(byte[] block)
    {
        var packed = BinaryPrimitives.ReadUInt64BigEndian(block.AsSpan(10, 8));

        var sampleRate = (int)((packed >> 44) & 0xFFFFF);
        var channels = (int)((packed >> 41) & 0x7) + 1;
        var bitsPerSample = (int)((packed >> 36) & 0x1F) + 1;
        var totalSamples = packed & 0xFFFFFFFFFUL;

        var duration = sampleRate > 0 && totalSamples > 0
            ? TimeSpan.FromSeconds((double)totalSamples / sampleRate)
            : TimeSpan.Zero;

        return (sampleRate, channels, bitsPerSample, duration);
    }

    /// <summary>
    /// A Vorbis comment block is little-endian throughout: a vendor string, a count, then that many
    /// length-prefixed UTF-8 "NAME=value" entries.
    /// </summary>
    private static MusicTagSet ParseVorbisComment(byte[] block)
    {
        var offset = 0;
        if (!TryReadLengthPrefixed(block, ref offset, out _))
        {
            return MusicTagSet.Empty;
        }

        if (offset + 4 > block.Length)
        {
            return MusicTagSet.Empty;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset, 4));
        offset += 4;

        List<MusicTag> tags = [];
        string title = string.Empty, artist = string.Empty, album = string.Empty;

        for (var i = 0; i < count; i++)
        {
            if (!TryReadLengthPrefixed(block, ref offset, out var entry))
            {
                break;
            }

            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var field = entry[..separator];
            var value = entry[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            switch (field.ToUpperInvariant())
            {
                case "TITLE":
                    title = value;
                    break;
                case "ARTIST":
                    artist = value;
                    break;
                case "ALBUM":
                    album = value;
                    break;
            }

            tags.Add(new MusicTag(
                FieldNames.TryGetValue(field, out var friendly) ? friendly : field,
                value));
        }

        if (tags.Count == 0)
        {
            return MusicTagSet.Empty;
        }

        return new MusicTagSet
        {
            Source = "Vorbis comment",
            Title = title,
            Artist = artist,
            Album = album,
            Tags = tags,
        };
    }

    private static bool TryReadLengthPrefixed(byte[] block, ref int offset, out string value)
    {
        value = string.Empty;
        if (offset + 4 > block.Length)
        {
            return false;
        }

        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset, 4));
        offset += 4;

        if (length < 0 || offset + length > block.Length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(block, offset, length);
        offset += length;
        return true;
    }

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
