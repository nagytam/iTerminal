using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;

namespace iTerminal.Core.Test.Services.Music;

[TestClass]
public class MusicTagReaderTest
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "TagReaderTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    [TestMethod]
    public void Read_Id3V2_ReturnsTitleArtistAndAlbum()
    {
        var path = TestAudioFiles.WriteId3V2File(_tempRoot, "tagged.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");

        var tags = MusicTagReader.Read(path);

        Assert.AreEqual("Comfortably Numb", tags.Title);
        Assert.AreEqual("Pink Floyd", tags.Artist);
        Assert.AreEqual("The Wall", tags.Album);
    }

    [TestMethod]
    public void Read_Id3V1_IsUsedWhenThereIsNoId3V2()
    {
        var path = TestAudioFiles.WriteId3V1File(_tempRoot, "old.mp3", "Old Title", "Old Artist", "Old Album");

        var tags = MusicTagReader.Read(path);

        Assert.AreEqual("Old Title", tags.Title);
        Assert.AreEqual("Old Artist", tags.Artist);
        Assert.AreEqual("Old Album", tags.Album);
    }

    [TestMethod]
    public void Read_UntaggedFile_ReturnsEmptyTags()
    {
        var path = Path.Combine(_tempRoot, "plain.mp3");
        File.WriteAllBytes(path, new byte[512]);

        Assert.IsTrue(MusicTagReader.Read(path).IsEmpty);
    }

    [TestMethod]
    public void Read_MissingFile_ReturnsEmptyTagsRatherThanThrowing()
    {
        Assert.IsTrue(MusicTagReader.Read(Path.Combine(_tempRoot, "nope.mp3")).IsEmpty);
    }

    [TestMethod]
    public void CreateTrack_UsesTagsForTheDisplayName()
    {
        var path = TestAudioFiles.WriteId3V2File(_tempRoot, "tagged.mp3", "Time", "Pink Floyd", "Dark Side");

        var track = MusicTagReader.CreateTrack(path);

        Assert.AreEqual("Pink Floyd - Time", track.DisplayName);
    }

    [TestMethod]
    public void CreateTrack_WithoutTags_FallsBackToTheFileName()
    {
        var path = Path.Combine(_tempRoot, "Some Song.mp3");
        File.WriteAllBytes(path, new byte[64]);

        var track = MusicTagReader.CreateTrack(path);

        Assert.AreEqual("Some Song", track.DisplayName);
    }

    // ── ReadAll: the whole tag block, for the music file viewer ──────────────

    [TestMethod]
    public void ReadAll_ListsEveryFrameUnderItsDisplayName()
    {
        var path = TestAudioFiles.WriteId3V2FileWithFrames(
            _tempRoot,
            "full.mp3",
            ("TIT2", "Time"),
            ("TPE1", "Pink Floyd"),
            ("TALB", "The Dark Side of the Moon"),
            ("TRCK", "4/10"),
            ("TYER", "1973"),
            ("TPUB", "Harvest"));

        var tags = MusicTagReader.ReadAll(path);

        Assert.AreEqual("ID3v2.3", tags.Source);
        Assert.AreEqual("Time", tags.Title);
        Assert.AreEqual("Pink Floyd", tags.Artist);
        CollectionAssert.AreEqual(
            new[] { "Title", "Artist", "Album", "Track", "Year", "Publisher" },
            tags.Tags.Select(tag => tag.Name).ToArray(),
            "Tags keep the order the file wrote them in");
        Assert.AreEqual("4/10", tags.Tags.Single(tag => tag.Name == "Track").Value);
    }

    [TestMethod]
    public void ReadAll_ShowsAnUnknownFrameUnderItsOwnId()
    {
        var path = TestAudioFiles.WriteId3V2FileWithFrames(_tempRoot, "odd.mp3", ("TZZZ", "something"));

        var tags = MusicTagReader.ReadAll(path);

        Assert.AreEqual("TZZZ", tags.Tags.Single().Name);
        Assert.AreEqual("something", tags.Tags.Single().Value);
    }

    [TestMethod]
    public void ReadAll_ResolvesANumericGenreToItsName()
    {
        var path = TestAudioFiles.WriteId3V2FileWithFrames(_tempRoot, "genre.mp3", ("TCON", "(17)"));

        var tags = MusicTagReader.ReadAll(path);

        Assert.AreEqual("Rock", tags.Tags.Single(tag => tag.Name == "Genre").Value);
    }

    [TestMethod]
    public void ReadAll_KeepsAGenreThatIsAlreadyAWord()
    {
        var path = TestAudioFiles.WriteId3V2FileWithFrames(_tempRoot, "genre2.mp3", ("TCON", "Shoegaze"));

        Assert.AreEqual("Shoegaze", MusicTagReader.ReadAll(path).Tags.Single().Value);
    }

    [TestMethod]
    public void ReadAll_ReadsTheTextOfACommentFrameWithoutItsDescription()
    {
        var path = TestAudioFiles.WriteId3V2FileWithComment(_tempRoot, "comment.mp3", "ripper", "Ripped from vinyl");

        var tags = MusicTagReader.ReadAll(path);

        Assert.AreEqual("Comment", tags.Tags.Single().Name);
        Assert.AreEqual("Ripped from vinyl", tags.Tags.Single().Value);
    }

    [TestMethod]
    public void ReadAll_Id3V1_IncludesYearTrackAndGenre()
    {
        var path = TestAudioFiles.WriteId3V1File(
            _tempRoot,
            "v11.mp3",
            "Old Title",
            "Old Artist",
            "Old Album",
            year: "1984",
            comment: "from a tape",
            track: 7,
            genre: 17);

        var tags = MusicTagReader.ReadAll(path);

        Assert.AreEqual("ID3v1.1", tags.Source);
        Assert.AreEqual("1984", tags.Tags.Single(tag => tag.Name == "Year").Value);
        Assert.AreEqual("7", tags.Tags.Single(tag => tag.Name == "Track").Value);
        Assert.AreEqual("Rock", tags.Tags.Single(tag => tag.Name == "Genre").Value);
        Assert.AreEqual("from a tape", tags.Tags.Single(tag => tag.Name == "Comment").Value);
    }

    [TestMethod]
    public void ReadAll_UntaggedFile_IsEmptyRatherThanThrowing()
    {
        var path = Path.Combine(_tempRoot, "plain.mp3");
        File.WriteAllBytes(path, new byte[512]);

        Assert.IsTrue(MusicTagReader.ReadAll(path).IsEmpty);
        Assert.IsTrue(MusicTagReader.ReadAll(Path.Combine(_tempRoot, "nope.mp3")).IsEmpty);
    }
}
