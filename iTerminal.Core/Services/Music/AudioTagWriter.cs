using System.Buffers.Binary;
using System.Text;

namespace iTerminal.Core.Services.Music;

/// <summary>The fields the tag editor sets. An empty field means "no such tag": a field that was cleared is removed from the file.</summary>
public sealed record AudioTagEdit(string Title, string Artist, string Album, string Year, string Track, string Genre, string Comment)
{
    public static readonly AudioTagEdit Empty = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// Writes the tags of an MP3 (its ID3v2 block) or a FLAC (its Vorbis comment block). Everything the edit does not touch is
/// kept as it was: other ID3 frames (cover art, lyrics), the other FLAC metadata blocks and comments, and the audio itself,
/// which is copied byte for byte. The file is written under a temporary name and moved over the original, so a failed save
/// leaves the original as it was.
///
/// <para>Other formats, and ID3v2.2 tags, are not written: <see cref="CanWrite"/> says which files can be.</para>
/// </summary>
public static class AudioTagWriter
{
    private static readonly string[] Mp3Extensions = [".mp3"];
    private static readonly string[] FlacExtensions = [".flac"];

    private const byte TextEncodingUtf16 = 1;
    private const int MaxTagBytes = 64 * 1024 * 1024;

    /// <summary>True for an MP3 or a FLAC file: the two formats whose tags are written here.</summary>
    public static bool CanWrite(string path)
    {
        string extension = Path.GetExtension(path);
        return Mp3Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || FlacExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Writes the edit into the file's tags. Throws <see cref="NotSupportedException"/> for a file this cannot write, and <see cref="InvalidDataException"/> for a tag it cannot read.</summary>
    public static void Write(string path, AudioTagEdit edit)
    {
        string extension = Path.GetExtension(path);
        if (Mp3Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            WriteId3(path, edit);
        }
        else if (FlacExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            WriteFlac(path, edit);
        }
        else
        {
            throw new NotSupportedException("Tags can be written to MP3 and FLAC files.");
        }
    }

    #region ID3v2 (MP3)

    /// <summary>The frames the edit writes. Each is dropped from the tag and written again from the edit when it has a value.</summary>
    private static readonly HashSet<string> Id3EditedFrames =
        ["TIT2", "TPE1", "TALB", "TYER", "TDRC", "TRCK", "TCON"];

    private const string Id3Comment = "COMM";

    private static void WriteId3(string path, AudioTagEdit edit)
    {
        byte major = 3;
        int tagEnd = 0;
        List<(string Id, byte Flags0, byte Flags1, byte[] Body)> kept = [];
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] header = new byte[10];
            if (stream.Read(header) == 10 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
            {
                if (header[3] == 2)
                {
                    throw new NotSupportedException("ID3v2.2 tags can't be edited; save the file in ID3v2.3 or v2.4 first.");
                }

                if (header[3] is 3 or 4)
                {
                    major = header[3];
                }

                int size = SynchsafeToInt(header.AsSpan(6));
                if (size > MaxTagBytes)
                {
                    throw new InvalidDataException("The ID3 tag is too large to rewrite.");
                }

                bool footer = major == 4 && (header[5] & 0x10) != 0;
                tagEnd = 10 + size + (footer ? 10 : 0);
                byte[] body = new byte[size];
                stream.ReadExactly(body);
                kept = ReadFrames(body, major);
            }
        }

        List<byte[]> frames = [];
        foreach ((string id, byte flags0, byte flags1, byte[] body) in kept)
        {
            // A comment with a description is a separate tag (iTunes writes several): only the plain comment is the one edited.
            bool replaced = Id3EditedFrames.Contains(id) || (id == Id3Comment && !HasDescription(body));
            if (!replaced)
            {
                frames.Add(Id3Frame(major, id, body, flags0, flags1));
            }
        }

        AddText(frames, major, "TIT2", edit.Title);
        AddText(frames, major, "TPE1", edit.Artist);
        AddText(frames, major, "TALB", edit.Album);
        AddText(frames, major, major == 4 ? "TDRC" : "TYER", edit.Year);
        AddText(frames, major, "TRCK", edit.Track);
        AddText(frames, major, "TCON", edit.Genre);
        if (edit.Comment.Length > 0)
        {
            frames.Add(Id3Frame(major, Id3Comment, CommentBody(edit.Comment), 0, 0));
        }

        int framesSize = frames.Sum(f => f.Length);
        byte[] tag = new byte[10 + framesSize];
        "ID3"u8.CopyTo(tag);
        tag[3] = major;
        IntToSynchsafe(framesSize, tag.AsSpan(6));
        int at = 10;
        foreach (byte[] frame in frames)
        {
            frame.CopyTo(tag, at);
            at += frame.Length;
        }

        ReplaceAudio(path, tag, startOffset: tagEnd);
    }

    private static void AddText(List<byte[]> frames, byte major, string id, string value)
    {
        if (value.Length > 0)
        {
            frames.Add(Id3Frame(major, id, TextBody(value), 0, 0));
        }
    }

    /// <summary>The frames of an ID3 block, each with its id, its two flag bytes and its body. A zero byte where an id starts is padding.</summary>
    private static List<(string Id, byte Flags0, byte Flags1, byte[] Body)> ReadFrames(byte[] body, byte major)
    {
        List<(string, byte, byte, byte[])> frames = [];
        int at = 0;
        while (at + 10 <= body.Length && body[at] != 0)
        {
            string id = Encoding.ASCII.GetString(body, at, 4);
            int size = major == 4 ? SynchsafeToInt(body.AsSpan(at + 4)) : (int)BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(at + 4));
            if (size < 0 || at + 10 + size > body.Length)
            {
                break;
            }

            frames.Add((id, body[at + 8], body[at + 9], body.AsSpan(at + 10, size).ToArray()));
            at += 10 + size;
        }

        return [.. frames];
    }

    /// <summary>A frame: its id, its size in the tag's own encoding (synchsafe in v2.4, plain in v2.3), its flags and its body.</summary>
    private static byte[] Id3Frame(byte major, string id, byte[] body, byte flags0, byte flags1)
    {
        byte[] frame = new byte[10 + body.Length];
        Encoding.ASCII.GetBytes(id).CopyTo(frame, 0);
        if (major == 4)
        {
            IntToSynchsafe(body.Length, frame.AsSpan(4));
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)body.Length);
        }

        frame[8] = flags0;
        frame[9] = flags1;
        body.CopyTo(frame, 10);
        return frame;
    }

    /// <summary>
    /// Whether a COMM frame has a description: the text between its language and the first empty terminator is not empty.
    /// Only the plain comment, with an empty description, is replaced by an edit.
    /// </summary>
    private static bool HasDescription(byte[] body)
    {
        if (body.Length < 4)
        {
            return false;
        }

        byte encoding = body[0];
        bool wide = encoding is 1 or 2;
        int at = 4;
        if (encoding == 1 && body.Length >= at + 2)
        {
            at += 2; // the byte order mark of UTF-16 text
        }

        int step = wide ? 2 : 1;
        for (int i = at; i + step <= body.Length; i += step)
        {
            if (body.AsSpan(i, step).IndexOfAnyExcept((byte)0) < 0)
            {
                return i > at;
            }
        }

        return false;
    }

    private static byte[] TextBody(string value)
    {
        byte[] text = Encoding.Unicode.GetBytes(value);
        return [TextEncodingUtf16, 0xFF, 0xFE, .. text];
    }

    private static byte[] CommentBody(string value)
    {
        // Encoding, the language (three letters), an empty description with its terminator, then the text.
        byte[] text = Encoding.Unicode.GetBytes(value);
        return [TextEncodingUtf16, (byte)'e', (byte)'n', (byte)'g', 0xFF, 0xFE, 0x00, 0x00, 0xFF, 0xFE, .. text];
    }

    private static int SynchsafeToInt(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];

    private static void IntToSynchsafe(int value, Span<byte> bytes)
    {
        bytes[0] = (byte)((value >> 21) & 0x7F);
        bytes[1] = (byte)((value >> 14) & 0x7F);
        bytes[2] = (byte)((value >> 7) & 0x7F);
        bytes[3] = (byte)(value & 0x7F);
    }

    #endregion

    #region FLAC

    private static readonly HashSet<string> FlacEditedComments =
        new(StringComparer.OrdinalIgnoreCase) { "TITLE", "ARTIST", "ALBUM", "DATE", "YEAR", "TRACKNUMBER", "GENRE", "COMMENT", "DESCRIPTION" };

    private const byte FlacVorbisComment = 4;
    private const byte FlacStreamInfo = 0;

    private static void WriteFlac(string path, AudioTagEdit edit)
    {
        byte[] head;
        int audioStart;
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] magic = new byte[4];
            stream.ReadExactly(magic);
            if (!magic.AsSpan().SequenceEqual("fLaC"u8))
            {
                throw new InvalidDataException("Not a FLAC file.");
            }

            List<(byte Type, byte[] Body)> blocks = [];
            bool last = false;
            while (!last)
            {
                byte[] blockHeader = new byte[4];
                stream.ReadExactly(blockHeader);
                last = (blockHeader[0] & 0x80) != 0;
                int length = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
                if (length > MaxTagBytes)
                {
                    throw new InvalidDataException("A FLAC metadata block is too large to rewrite.");
                }

                byte[] body = new byte[length];
                stream.ReadExactly(body);
                blocks.Add(((byte)(blockHeader[0] & 0x7F), body));
            }

            audioStart = (int)stream.Position;
            head = BuildFlacHead(blocks, edit);
        }

        ReplaceAudio(path, head, startOffset: audioStart);
    }

    /// <summary>The "fLaC" marker and every metadata block, with the Vorbis comment block rebuilt for the edit.</summary>
    private static byte[] BuildFlacHead(List<(byte Type, byte[] Body)> blocks, AudioTagEdit edit)
    {
        int vorbis = blocks.FindIndex(b => b.Type == FlacVorbisComment);
        byte[] comments = vorbis >= 0 ? RebuildComments(blocks[vorbis].Body, edit) : RebuildComments(null, edit);
        if (vorbis >= 0)
        {
            blocks[vorbis] = (FlacVorbisComment, comments);
        }
        else
        {
            int afterStreamInfo = blocks.FindIndex(b => b.Type == FlacStreamInfo) + 1;
            blocks.Insert(Math.Max(0, afterStreamInfo), (FlacVorbisComment, comments));
        }

        using MemoryStream head = new();
        head.Write("fLaC"u8);
        for (int i = 0; i < blocks.Count; i++)
        {
            (byte type, byte[] body) = blocks[i];
            bool isLast = i == blocks.Count - 1;
            head.WriteByte((byte)((isLast ? 0x80 : 0) | type));
            head.WriteByte((byte)(body.Length >> 16));
            head.WriteByte((byte)(body.Length >> 8));
            head.WriteByte((byte)body.Length);
            head.Write(body);
        }

        return head.ToArray();
    }

    /// <summary>
    /// The comment block with the edited fields replaced and every other comment kept: the vendor string, then each comment
    /// as "NAME=value" in UTF-8, with the lengths little-endian.
    /// </summary>
    private static byte[] RebuildComments(byte[]? existing, AudioTagEdit edit)
    {
        string vendor = "iTerminal";
        List<string> kept = [];
        if (existing is not null && existing.Length >= 8)
        {
            int at = 0;
            int vendorLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(existing);
            vendor = Encoding.UTF8.GetString(existing, 4, vendorLength);
            at = 4 + vendorLength;
            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(existing.AsSpan(at));
            at += 4;
            for (int i = 0; i < count && at + 4 <= existing.Length; i++)
            {
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(existing.AsSpan(at));
                string comment = Encoding.UTF8.GetString(existing, at + 4, length);
                at += 4 + length;
                int equals = comment.IndexOf('=');
                string name = equals < 0 ? comment : comment[..equals];
                bool replaced = FlacEditedComments.Contains(name) && IsReplaced(name, edit);
                if (!replaced)
                {
                    kept.Add(comment);
                }
            }
        }

        AddComment(kept, "TITLE", edit.Title);
        AddComment(kept, "ARTIST", edit.Artist);
        AddComment(kept, "ALBUM", edit.Album);
        AddComment(kept, "DATE", edit.Year);
        AddComment(kept, "TRACKNUMBER", edit.Track);
        AddComment(kept, "GENRE", edit.Genre);
        AddComment(kept, "COMMENT", edit.Comment);

        using MemoryStream block = new();
        byte[] vendorBytes = Encoding.UTF8.GetBytes(vendor);
        WriteLe32(block, vendorBytes.Length);
        block.Write(vendorBytes);
        WriteLe32(block, kept.Count);
        foreach (string comment in kept)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(comment);
            WriteLe32(block, bytes.Length);
            block.Write(bytes);
        }

        return block.ToArray();
    }

    /// <summary>A kept comment is replaced when the edit sets the field it names. The year replaces both of its names.</summary>
    private static bool IsReplaced(string name, AudioTagEdit edit) => name.ToUpperInvariant() switch
    {
        "TITLE" => edit.Title.Length > 0,
        "ARTIST" => edit.Artist.Length > 0,
        "ALBUM" => edit.Album.Length > 0,
        "DATE" or "YEAR" => edit.Year.Length > 0,
        "TRACKNUMBER" => edit.Track.Length > 0,
        "GENRE" => edit.Genre.Length > 0,
        "COMMENT" or "DESCRIPTION" => edit.Comment.Length > 0,
        _ => false,
    };

    private static void AddComment(List<string> comments, string name, string value)
    {
        if (value.Length > 0)
        {
            comments.Add($"{name}={value}");
        }
    }

    private static void WriteLe32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
        stream.Write(bytes);
    }

    #endregion

    /// <summary>
    /// Writes <paramref name="head"/> and then the original file from <paramref name="startOffset"/> on, to a temporary file
    /// beside the original, then moves it over the original. The original is untouched until that move.
    /// </summary>
    private static void ReplaceAudio(string path, byte[] head, int startOffset)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream source = File.OpenRead(path))
            using (FileStream target = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                target.Write(head);
                source.Seek(startOffset, SeekOrigin.Begin);
                source.CopyTo(target);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
                // The temporary file is left behind only when it cannot be removed; the original is intact either way.
            }
        }
    }
}
