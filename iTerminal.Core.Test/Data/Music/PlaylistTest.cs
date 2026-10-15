using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Test.Data.Music;

[TestClass]
public class PlaylistTest
{
    private static Playlist CreatePlaylist(params string[] paths)
    {
        var playlist = new Playlist();
        playlist.AddRange(paths.Select(p => new MusicTrack(p)));
        return playlist;
    }

    [TestMethod]
    public void Add_IgnoresDuplicatePaths()
    {
        var playlist = new Playlist();

        Assert.IsTrue(playlist.Add(new MusicTrack(@"C:\music\a.mp3")));
        Assert.IsFalse(playlist.Add(new MusicTrack(@"C:\music\a.mp3")));
        Assert.IsFalse(playlist.Add(new MusicTrack(@"C:\MUSIC\A.MP3")), "Paths differ only by case");
        Assert.AreEqual(1, playlist.Count);
    }

    [TestMethod]
    public void AddRange_ReturnsOnlyTheNumberActuallyAdded()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3");

        var added = playlist.AddRange([new MusicTrack("b.mp3"), new MusicTrack("c.mp3")]);

        Assert.AreEqual(1, added);
        Assert.AreEqual(3, playlist.Count);
    }

    [TestMethod]
    public void RemoveAt_BelowCurrent_KeepsCursorOnTheSameTrack()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3", "c.mp3");
        playlist.CurrentIndex = 2;

        playlist.RemoveAt(0);

        Assert.AreEqual(1, playlist.CurrentIndex);
        Assert.AreEqual("c.mp3", playlist.CurrentTrack?.Path);
    }

    [TestMethod]
    public void RemoveAt_TheCurrentTrack_ClearsTheCursor()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3");
        playlist.CurrentIndex = 1;

        playlist.RemoveAt(1);

        Assert.AreEqual(-1, playlist.CurrentIndex);
        Assert.IsNull(playlist.CurrentTrack);
    }

    [TestMethod]
    public void Move_SwapsRowsAndFollowsTheCurrentTrack()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3", "c.mp3");
        playlist.CurrentIndex = 0;

        Assert.IsTrue(playlist.Move(0, 1));

        Assert.AreEqual("b.mp3", playlist[0]?.Path);
        Assert.AreEqual("a.mp3", playlist[1]?.Path);
        Assert.AreEqual(1, playlist.CurrentIndex, "The cursor follows the moved track");
    }

    [TestMethod]
    public void Move_OutOfRange_IsRejected()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3");

        Assert.IsFalse(playlist.Move(0, -1));
        Assert.IsFalse(playlist.Move(1, 1));
        Assert.IsFalse(playlist.Move(0, 0));
    }

    [TestMethod]
    public void GetNextIndex_RepeatOff_StopsAfterTheLastTrack()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3");
        playlist.CurrentIndex = 1;

        Assert.AreEqual(-1, playlist.GetNextIndex(RepeatMode.Off));
    }

    [TestMethod]
    public void GetNextIndex_RepeatAll_WrapsToTheStart()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3");
        playlist.CurrentIndex = 1;

        Assert.AreEqual(0, playlist.GetNextIndex(RepeatMode.All));
    }

    [TestMethod]
    public void GetNextIndex_RepeatOne_ReturnsTheCurrentTrack()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3", "c.mp3");
        playlist.CurrentIndex = 1;

        Assert.AreEqual(1, playlist.GetNextIndex(RepeatMode.One));
    }

    [TestMethod]
    public void GetNextIndex_WithNothingCued_StartsAtTheFirstTrack()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3");

        Assert.AreEqual(0, playlist.GetNextIndex(RepeatMode.Off));
    }

    [TestMethod]
    public void GetPreviousIndex_AtTheStart_WrapsOnlyWhenRepeatingAll()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3", "c.mp3");
        playlist.CurrentIndex = 0;

        Assert.AreEqual(-1, playlist.GetPreviousIndex(RepeatMode.Off));
        Assert.AreEqual(2, playlist.GetPreviousIndex(RepeatMode.All));
    }

    [TestMethod]
    public void Shuffle_KeepsPlayingTrackFirstSoItIsNotInterrupted()
    {
        var playlist = CreatePlaylist([.. Enumerable.Range(0, 50).Select(i => $"track{i}.mp3")]);
        playlist.CurrentIndex = 17;

        playlist.IsShuffled = true;

        // Whatever the permutation is, the next track must not be the one already playing.
        var next = playlist.GetNextIndex(RepeatMode.All);
        Assert.AreNotEqual(17, next);
        Assert.IsTrue(next >= 0 && next < 50);
    }

    [TestMethod]
    public void Shuffle_VisitsEveryTrackExactlyOnceBeforeWrapping()
    {
        var playlist = CreatePlaylist([.. Enumerable.Range(0, 10).Select(i => $"track{i}.mp3")]);
        playlist.IsShuffled = true;
        playlist.CurrentIndex = playlist.GetNextIndex(RepeatMode.Off);

        var visited = new HashSet<int> { playlist.CurrentIndex };
        for (int i = 0; i < 9; i++)
        {
            var next = playlist.GetNextIndex(RepeatMode.Off);
            Assert.IsTrue(next >= 0, "Shuffled traversal ended early");
            Assert.IsTrue(visited.Add(next), $"Track {next} was played twice in one pass");
            playlist.CurrentIndex = next;
        }

        Assert.AreEqual(10, visited.Count);
        Assert.AreEqual(-1, playlist.GetNextIndex(RepeatMode.Off), "The shuffled pass is over");
    }

    [TestMethod]
    public void UnShuffle_RestoresSequentialOrder()
    {
        var playlist = CreatePlaylist("a.mp3", "b.mp3", "c.mp3");
        playlist.IsShuffled = true;
        playlist.IsShuffled = false;
        playlist.CurrentIndex = 0;

        Assert.AreEqual(1, playlist.GetNextIndex(RepeatMode.Off));
    }

    [TestMethod]
    public void Version_ChangesOnEveryMutation()
    {
        var playlist = new Playlist();
        var versions = new List<int> { playlist.Version };

        playlist.Add(new MusicTrack("a.mp3"));
        versions.Add(playlist.Version);

        playlist.AddRange([new MusicTrack("b.mp3")]);
        versions.Add(playlist.Version);

        playlist.Move(0, 1);
        versions.Add(playlist.Version);

        playlist.UpdateAt(0, new MusicTrack("b.mp3") { Title = "B" });
        versions.Add(playlist.Version);

        playlist.RemoveAt(0);
        versions.Add(playlist.Version);

        playlist.Clear();
        versions.Add(playlist.Version);

        CollectionAssert.AllItemsAreUnique(versions);
    }

    [TestMethod]
    public void ReplaceAll_ResetsTheCursorAndDropsDuplicates()
    {
        var playlist = CreatePlaylist("a.mp3");
        playlist.CurrentIndex = 0;

        playlist.ReplaceAll([new MusicTrack("x.mp3"), new MusicTrack("y.mp3"), new MusicTrack("x.mp3")]);

        Assert.AreEqual(2, playlist.Count);
        Assert.AreEqual(-1, playlist.CurrentIndex);
    }

    [TestMethod]
    public void EmptyPlaylist_HasNoNextOrPrevious()
    {
        var playlist = new Playlist();

        Assert.AreEqual(-1, playlist.GetNextIndex(RepeatMode.All));
        Assert.AreEqual(-1, playlist.GetPreviousIndex(RepeatMode.All));
        Assert.IsNull(playlist.CurrentTrack);
    }
}
