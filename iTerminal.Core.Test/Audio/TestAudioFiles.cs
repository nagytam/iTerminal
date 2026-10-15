using System.Buffers.Binary;
using System.Text;

namespace iTerminal.Core.Test.Audio;

/// <summary>
/// Builds real audio files on disk for the decoder tests. Synthesising the bytes here (rather than
/// checking in binary fixtures) keeps the expected sample values visible in the assertions.
/// </summary>
internal static class TestAudioFiles
{
    internal const int FormatPcm = 1;
    internal const int FormatIeeeFloat = 3;

    /// <summary>Writes a WAVE file and returns its path.</summary>
    internal static string WriteWave(
        string directory,
        string fileName,
        byte[] sampleData,
        int sampleRate = 44100,
        int channels = 2,
        int bitsPerSample = 16,
        int formatTag = FormatPcm)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, BuildWave(sampleData, sampleRate, channels, bitsPerSample, formatTag));
        return path;
    }

    internal static byte[] BuildWave(
        byte[] sampleData,
        int sampleRate = 44100,
        int channels = 2,
        int bitsPerSample = 16,
        int formatTag = FormatPcm)
    {
        var blockAlign = channels * (bitsPerSample / 8);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + sampleData.Length);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)formatTag);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(sampleData.Length);
        writer.Write(sampleData);

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Interleaved 16-bit samples as raw little-endian bytes.</summary>
    internal static byte[] Pcm16(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }
        return bytes;
    }

    /// <summary>A silent 16-bit stereo WAVE of the requested length, for timing assertions.</summary>
    internal static string WriteSilence(string directory, string fileName, TimeSpan duration, int sampleRate = 8000)
    {
        var frames = (int)(duration.TotalSeconds * sampleRate);
        return WriteWave(directory, fileName, new byte[frames * 4], sampleRate, channels: 2, bitsPerSample: 16);
    }

    /// <summary>
    /// Writes a file carrying an ID3v2.3 tag followed by junk. Only the tag is parsed by
    /// <c>MusicTagReader</c>, so the payload does not need to be real MPEG audio.
    /// </summary>
    internal static string WriteId3V2File(string directory, string fileName, string title, string artist, string album)
    {
        var frames = new List<byte>();
        AppendTextFrame(frames, "TIT2", title);
        AppendTextFrame(frames, "TPE1", artist);
        AppendTextFrame(frames, "TALB", album);

        return WriteId3V2Container(directory, fileName, frames);
    }

    /// <summary>
    /// Writes a file carrying an ID3v2.3 tag built from arbitrary text frames, for the cases where
    /// the three well-known ones are not the point (genre codes, comments, exotic frame ids).
    /// </summary>
    internal static string WriteId3V2FileWithFrames(
        string directory,
        string fileName,
        params (string FrameId, string Value)[] textFrames)
    {
        var frames = new List<byte>();
        foreach ((string frameId, string value) in textFrames)
        {
            AppendTextFrame(frames, frameId, value);
        }

        return WriteId3V2Container(directory, fileName, frames);
    }

    /// <summary>Writes a file whose ID3v2.3 tag holds one comment frame (encoding, language, description, text).</summary>
    internal static string WriteId3V2FileWithComment(string directory, string fileName, string description, string comment)
    {
        var payload = new List<byte> { 0 }; // ISO-8859-1
        payload.AddRange("eng"u8.ToArray());
        payload.AddRange(Encoding.Latin1.GetBytes(description));
        payload.Add(0);
        payload.AddRange(Encoding.Latin1.GetBytes(comment));

        var frames = new List<byte>();
        frames.AddRange(Encoding.ASCII.GetBytes("COMM"));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)payload.Count);
        frames.AddRange(size);
        frames.AddRange(new byte[2]); // flags
        frames.AddRange(payload);

        return WriteId3V2Container(directory, fileName, frames);
    }

    /// <summary>Writes a file whose final 128 bytes are an ID3v1 tag.</summary>
    internal static string WriteId3V1File(
        string directory,
        string fileName,
        string title,
        string artist,
        string album,
        string year = "",
        string comment = "",
        byte track = 0,
        byte genre = 255)
    {
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        WriteFixed(tag.AsSpan(3, 30), title);
        WriteFixed(tag.AsSpan(33, 30), artist);
        WriteFixed(tag.AsSpan(63, 30), album);
        WriteFixed(tag.AsSpan(93, 4), year);
        WriteFixed(tag.AsSpan(97, 28), comment);
        if (track > 0)
        {
            // ID3v1.1: the last two comment bytes become a zero marker and the track number.
            tag[125] = 0;
            tag[126] = track;
        }

        tag[127] = genre;

        var content = new byte[256 + tag.Length];
        tag.CopyTo(content, 256);

        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>
    /// Writes a FLAC file that is nothing but its metadata blocks — a STREAMINFO and a Vorbis
    /// comment. <c>FlacTagReader</c> never looks at the audio frames, so there is no need to
    /// synthesise any.
    /// </summary>
    internal static string WriteFlac(
        string directory,
        string fileName,
        int sampleRate = 44100,
        int channels = 2,
        int bitsPerSample = 16,
        long totalSamples = 44100,
        params (string Field, string Value)[] tags)
    {
        var content = new List<byte>();
        content.AddRange("fLaC"u8.ToArray());

        var streamInfo = new byte[34];
        var packed = ((ulong)sampleRate << 44)
            | ((ulong)(channels - 1) << 41)
            | ((ulong)(bitsPerSample - 1) << 36)
            | ((ulong)totalSamples & 0xFFFFFFFFFUL);
        BinaryPrimitives.WriteUInt64BigEndian(streamInfo.AsSpan(10, 8), packed);
        AppendMetadataBlock(content, blockType: 0, isLast: false, streamInfo);

        var comment = new List<byte>();
        AppendVorbisString(comment, "iTerminal test");
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)tags.Length);
        comment.AddRange(count);
        foreach ((string field, string value) in tags)
        {
            AppendVorbisString(comment, $"{field}={value}");
        }

        AppendMetadataBlock(content, blockType: 4, isLast: true, [.. comment]);

        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, [.. content]);
        return path;
    }

    private static void AppendMetadataBlock(List<byte> content, int blockType, bool isLast, byte[] block)
    {
        content.Add((byte)((isLast ? 0x80 : 0) | blockType));
        content.Add((byte)((block.Length >> 16) & 0xFF));
        content.Add((byte)((block.Length >> 8) & 0xFF));
        content.Add((byte)(block.Length & 0xFF));
        content.AddRange(block);
    }

    private static void AppendVorbisString(List<byte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
        target.AddRange(length);
        target.AddRange(bytes);
    }

    private static string WriteId3V2Container(string directory, string fileName, List<byte> frames)
    {
        var content = new List<byte>();
        content.AddRange("ID3"u8.ToArray());
        content.Add(3); // version 2.3
        content.Add(0); // revision
        content.Add(0); // flags
        content.AddRange(SynchSafe(frames.Count));
        content.AddRange(frames);
        content.AddRange(new byte[64]);

        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, [.. content]);
        return path;
    }

    private static void AppendTextFrame(List<byte> frames, string frameId, string value)
    {
        var payload = new List<byte> { 0 }; // ISO-8859-1
        payload.AddRange(Encoding.Latin1.GetBytes(value));

        frames.AddRange(Encoding.ASCII.GetBytes(frameId));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)payload.Count);
        frames.AddRange(size);
        frames.AddRange(new byte[2]); // flags
        frames.AddRange(payload);
    }

    private static void WriteFixed(Span<byte> destination, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        var length = Math.Min(bytes.Length, destination.Length);
        bytes.AsSpan(0, length).CopyTo(destination);
    }

    private static byte[] SynchSafe(int value) =>
    [
        (byte)((value >> 21) & 0x7F),
        (byte)((value >> 14) & 0x7F),
        (byte)((value >> 7) & 0x7F),
        (byte)(value & 0x7F),
    ];
}
