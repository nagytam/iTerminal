using System.Buffers.Binary;
using System.Text;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// Writing MP3 (ID3v2) and FLAC (Vorbis comment) tags. The files are built here, so the tests know exactly what the
/// writer must keep: other frames, other comments, the metadata blocks, and the audio, which must come through byte for byte.
/// </summary>
[TestClass]
public class AudioTagWriterTest
{
    private static readonly byte[] Audio = Enumerable.Range(0, 300).Select(i => (byte)(i * 7 + 3)).ToArray();

    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tagwriter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
        }
    }

    #region building files

    private static byte[] Synchsafe(int value) =>
        [(byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F), (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)];

    /// <summary>An ID3v2.3 frame: id, plain size, two flag bytes, body.</summary>
    private static byte[] Frame3(string id, byte[] body)
    {
        byte[] size = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)body.Length);
        return [.. Encoding.ASCII.GetBytes(id), .. size, 0, 0, .. body];
    }

    private static byte[] Utf16Text(string text) => [1, 0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];

    private static byte[] Id3Tag(params byte[][] frames)
    {
        byte[] body = frames.SelectMany(f => f).ToArray();
        return [.. Encoding.ASCII.GetBytes("ID3"), 3, 0, 0, .. Synchsafe(body.Length), .. body];
    }

    private string WriteMp3(params byte[][] frames)
    {
        string path = Path.Combine(_directory, "song.mp3");
        File.WriteAllBytes(path, [.. (frames.Length == 0 ? [] : Id3Tag(frames)), .. Audio]);
        return path;
    }

    private static byte[] Vorbis(string vendor, params string[] comments)
    {
        List<byte> bytes = [];
        byte[] v = Encoding.UTF8.GetBytes(vendor);
        bytes.AddRange(BitConverter.GetBytes(v.Length));
        bytes.AddRange(v);
        bytes.AddRange(BitConverter.GetBytes(comments.Length));
        foreach (string comment in comments)
        {
            byte[] c = Encoding.UTF8.GetBytes(comment);
            bytes.AddRange(BitConverter.GetBytes(c.Length));
            bytes.AddRange(c);
        }

        return [.. bytes];
    }

    private static byte[] Block(byte type, bool last, byte[] body) =>
        [(byte)((last ? 0x80 : 0) | type), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length, .. body];

    private string WriteFlac(params byte[][] blocks)
    {
        string path = Path.Combine(_directory, "song.flac");
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes("fLaC"), .. blocks.SelectMany(b => b), .. Audio]);
        return path;
    }

    private static byte[] StreamInfo(bool last) =>
        Block(0, last, new byte[34]);

    #endregion

    private static AudioTagEdit Edit(string title = "", string artist = "", string album = "", string year = "", string track = "", string genre = "", string comment = "") =>
        new(title, artist, album, year, track, genre, comment);

    [TestMethod]
    public void Mp3_TheEditedFieldsReadBack_AndTheAudioIsUnchanged()
    {
        string path = WriteMp3(Frame3("TIT2", Utf16Text("Old")), Frame3("TPE1", Utf16Text("Someone")));

        AudioTagWriter.Write(path, Edit("New title", "Ann", "Album", "2024", "3/12", "Jazz", "hello"));

        var tags = MusicTagReader.ReadAll(path);
        Assert.AreEqual("New title", tags.Title);
        Assert.AreEqual("Ann", tags.Artist);
        Assert.AreEqual("Album", tags.Album);
        Assert.AreEqual("2024", tags.Tags.Single(t => t.Name == "Year").Value);
        Assert.AreEqual("3/12", tags.Tags.Single(t => t.Name == "Track").Value);
        Assert.AreEqual("Jazz", tags.Tags.Single(t => t.Name == "Genre").Value);
        Assert.AreEqual("hello", tags.Tags.Single(t => t.Name == "Comment").Value);
        CollectionAssert.AreEqual(Audio, File.ReadAllBytes(path)[^Audio.Length..]);
    }

    [TestMethod]
    public void Mp3_OtherFrames_SuchAsCoverArt_AreKept()
    {
        byte[] picture = [0, .. Encoding.ASCII.GetBytes("image/png"), 0, 3, 0, .. new byte[] { 1, 2, 3, 4 }];
        string path = WriteMp3(Frame3("TIT2", Utf16Text("Old")), Frame3("APIC", picture));

        AudioTagWriter.Write(path, Edit("Changed"));

        byte[] written = File.ReadAllBytes(path);
        Assert.IsTrue(Encoding.ASCII.GetBytes("APIC").Length > 0 && IndexOf(written, Encoding.ASCII.GetBytes("APIC")) >= 0, "the cover art frame is kept");
        Assert.IsTrue(IndexOf(written, picture) >= 0, "and its bytes come through unchanged");
    }

    [TestMethod]
    public void Mp3_ACommentWithADescription_IsKept_WhileTheEditedCommentIsReplaced()
    {
        byte[] iTunes = [1, (byte)'e', (byte)'n', (byte)'g', 0xFF, 0xFE, (byte)'i', 0, 0, 0, 0xFF, 0xFE, (byte)'x', 0];
        byte[] plain = [1, (byte)'e', (byte)'n', (byte)'g', 0xFF, 0xFE, 0, 0, 0xFF, 0xFE, (byte)'o', 0];
        string path = WriteMp3(Frame3("COMM", iTunes), Frame3("COMM", plain));

        AudioTagWriter.Write(path, Edit(comment: "fresh"));

        byte[] written = File.ReadAllBytes(path);
        Assert.IsTrue(IndexOf(written, iTunes) >= 0, "the comment with a description is kept");
        Assert.IsTrue(IndexOf(written, Encoding.Unicode.GetBytes("fresh")) >= 0);
        Assert.IsTrue(IndexOf(written, plain) < 0, "the plain comment it replaces is gone");
    }

    [TestMethod]
    public void Mp3_WithNoTagYet_TheTagIsCreated()
    {
        string path = WriteMp3();

        AudioTagWriter.Write(path, Edit("First", "Ann"));

        var tags = MusicTagReader.ReadAll(path);
        Assert.AreEqual("First", tags.Title);
        Assert.AreEqual("Ann", tags.Artist);
        CollectionAssert.AreEqual(Audio, File.ReadAllBytes(path)[^Audio.Length..]);
    }

    [TestMethod]
    public void Mp3_AFieldThatIsCleared_IsRemovedFromTheFile()
    {
        string path = WriteMp3(Frame3("TIT2", Utf16Text("Title")), Frame3("TPE1", Utf16Text("Artist")));

        AudioTagWriter.Write(path, Edit(title: "Title"));

        var tags = MusicTagReader.ReadAll(path);
        Assert.AreEqual("Title", tags.Title);
        Assert.AreEqual(string.Empty, tags.Artist);
    }

    [TestMethod]
    public void Flac_TheEditedFieldsReadBack_AndOtherCommentsAreKept()
    {
        string path = WriteFlac(
            StreamInfo(last: false),
            Block(4, last: true, Vorbis("encoder 1.2", "TITLE=Old", "ARTIST=Someone", "CUSTOM=keep me")));

        AudioTagWriter.Write(path, Edit("New", "Ann", "Album", "2023", "7", "Folk", "note"));

        var info = FlacTagReader.Read(path);
        Assert.AreEqual("New", info.Tags.Title);
        Assert.AreEqual("Ann", info.Tags.Artist);
        Assert.AreEqual("Album", info.Tags.Album);
        Assert.IsTrue(info.Tags.Tags.Any(t => t.Value == "keep me"), "a comment the edit does not name is kept");
        CollectionAssert.AreEqual(Audio, File.ReadAllBytes(path)[^Audio.Length..]);
    }

    [TestMethod]
    public void Flac_WithNoCommentBlock_OneIsAddedAfterTheStreamInfo()
    {
        string path = WriteFlac(StreamInfo(last: true));

        AudioTagWriter.Write(path, Edit("Added"));

        var info = FlacTagReader.Read(path);
        Assert.AreEqual("Added", info.Tags.Title);
        CollectionAssert.AreEqual(Audio, File.ReadAllBytes(path)[^Audio.Length..]);
    }

    [TestMethod]
    public void Flac_NotAFlacFile_IsRefused()
    {
        string path = Path.Combine(_directory, "fake.flac");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RIFFnot flac"));

        Assert.ThrowsExactly<InvalidDataException>(() => AudioTagWriter.Write(path, Edit("x")));
        Assert.AreEqual("RIFFnot flac", Encoding.ASCII.GetString(File.ReadAllBytes(path)), "the file is untouched");
    }

    [TestMethod]
    public void AnUnsupportedFormat_IsRefused_AndCanWriteSaysSo()
    {
        string path = Path.Combine(_directory, "song.ogg");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.IsFalse(AudioTagWriter.CanWrite(path));
        Assert.ThrowsExactly<NotSupportedException>(() => AudioTagWriter.Write(path, Edit("x")));
        Assert.IsTrue(AudioTagWriter.CanWrite("track.MP3"));
        Assert.IsTrue(AudioTagWriter.CanWrite("track.flac"));
    }

    [TestMethod]
    public void AfterASave_NoTemporaryFileIsLeftBehind()
    {
        string path = WriteMp3(Frame3("TIT2", Utf16Text("Old")));

        AudioTagWriter.Write(path, Edit("New"));

        CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(_directory));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
