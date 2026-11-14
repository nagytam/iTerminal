using iTerminal.Core.Services.YouTube;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.YouTube;

[TestClass]
public class YtDlpYouTubeServiceTest
{
    private const string Id = "dQw4w9WgXcQ";

    private const string SearchLines =
        """
        {"id":"abcdefghijk","title":"First video","channel":"Some channel","duration":185,"view_count":1234,"upload_date":"20260102"}
        not json at all
        {"id":"bad id","title":"Rejected, the id has a space"}
        {"id":"lmnopqrstuv","title":"No channel field","uploader":"Uploader name"}
        {"id":"wxyzABCDEFG","title":""}
        {"id":"HIJKLMNOPQR","title":"Live, no duration","duration":null,"view_count":"many"}
        """;

    /// <summary>What a fake yt-dlp run answers, and the arguments it was called with.</summary>
    private sealed class FakeRunner(YtDlpYouTubeService.YtDlpResult result)
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public YtDlpYouTubeService.YtDlpRunner Run => (arguments, timeout, cancellationToken) =>
        {
            Calls.Add(arguments);
            return Task.FromResult(result);
        };
    }

    private static YtDlpYouTubeService Create(FakeRunner runner, bool installed = true) =>
        new(runner.Run, _ => installed ? "/bin/yt-dlp" : null);

    [TestMethod]
    public void ParseSearchOutput_KeepsTheVideosThatHaveAnIdAndATitle()
    {
        IReadOnlyList<YouTubeVideo> videos = YtDlpYouTubeService.ParseSearchOutput(SearchLines);

        Assert.AreEqual(3, videos.Count);
        YouTubeVideo first = videos[0];
        Assert.AreEqual("abcdefghijk", first.Id);
        Assert.AreEqual("First video", first.Title);
        Assert.AreEqual("Some channel", first.Channel);
        Assert.AreEqual(TimeSpan.FromSeconds(185), first.Duration);
        Assert.AreEqual(1234L, first.Views);
        Assert.AreEqual(new DateTime(2026, 1, 2), first.Published);
        Assert.AreEqual("https://www.youtube.com/watch?v=abcdefghijk", first.PageUrl);
    }

    [TestMethod]
    public void ParseSearchOutput_FallsBackToTheUploaderAndLeavesUnknownFieldsEmpty()
    {
        IReadOnlyList<YouTubeVideo> videos = YtDlpYouTubeService.ParseSearchOutput(SearchLines);

        YouTubeVideo uploaded = videos.Single(video => video.Id == "lmnopqrstuv");
        Assert.AreEqual("Uploader name", uploaded.Channel);
        Assert.IsNull(uploaded.Duration);
        Assert.IsNull(uploaded.Views);
        Assert.IsNull(uploaded.Published);

        YouTubeVideo live = videos.Single(video => video.Id == "HIJKLMNOPQR");
        Assert.IsNull(live.Duration);
        Assert.IsNull(live.Views, "a view count that is not a number is not shown as one");
    }

    [TestMethod]
    public void ParseSearchOutput_OfNothingIsNothing() =>
        CollectionAssert.AreEqual(Array.Empty<YouTubeVideo>(), YtDlpYouTubeService.ParseSearchOutput(string.Empty).ToList());

    [TestMethod]
    [DataRow("dQw4w9WgXcQ", true)]
    [DataRow("abc-DEF_123", true)]
    [DataRow("short", false)]
    [DataRow("dQw4w9WgXcQQ", false)]
    [DataRow("--verbose!!", false)]
    [DataRow("../../etc/x", false)]
    [DataRow("", false)]
    public void IsVideoId_AcceptsOnlyTheElevenCharacterIdShape(string id, bool expected) =>
        Assert.AreEqual(expected, YtDlpYouTubeService.IsVideoId(id));

    [TestMethod]
    public async Task Search_PassesTheTermAsOneArgumentAfterTheEndOfOptions()
    {
        FakeRunner runner = new(new(true, SearchLines, string.Empty));
        YtDlpYouTubeService service = Create(runner);

        IReadOnlyList<YouTubeVideo> videos = await service.SearchAsync("  --exec rm  ", 3);

        IReadOnlyList<string> arguments = runner.Calls.Single();
        int separator = arguments.ToList().IndexOf("--");
        Assert.IsTrue(separator > 0, "the term must follow the end-of-options marker");
        Assert.AreEqual("ytsearch3:--exec rm", arguments[separator + 1], "the term is trimmed and kept as one argument");
        Assert.AreEqual(3, videos.Count);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(500, 50)]
    [DataRow(25, 25)]
    public async Task Search_ClampsTheNumberOfResults(int requested, int expected)
    {
        FakeRunner runner = new(new(true, string.Empty, string.Empty));
        await Create(runner).SearchAsync("tulip", requested);

        StringAssert.Contains(string.Join(' ', runner.Calls.Single()), $"ytsearch{expected}:tulip");
    }

    [TestMethod]
    public async Task Search_OfBlankTextFindsNothingAndRunsNothing()
    {
        FakeRunner runner = new(new(true, SearchLines, string.Empty));

        IReadOnlyList<YouTubeVideo> videos = await Create(runner).SearchAsync("   ", 25);

        Assert.AreEqual(0, videos.Count);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task Search_ReportsYtDlpsOwnErrorLine()
    {
        FakeRunner runner = new(new(false, string.Empty, "WARNING: something\nERROR: [youtube] Sign in to confirm\n"));

        YouTubeException exception = await AssertAsync.ThrowsAsync<YouTubeException>(() => Create(runner).SearchAsync("tulip", 25));

        Assert.AreEqual("The search failed: [youtube] Sign in to confirm", exception.Message);
    }

    [TestMethod]
    public async Task Search_WithoutYtDlpOnThePathSaysSoAndRunsNothing()
    {
        FakeRunner runner = new(new(true, SearchLines, string.Empty));

        YouTubeException exception = await AssertAsync.ThrowsAsync<YouTubeException>(() => Create(runner, installed: false).SearchAsync("tulip", 25));

        StringAssert.Contains(exception.Message, "yt-dlp");
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task Search_ThatIsCancelledSurfacesTheCancellation()
    {
        FakeRunner runner = new(new(false, string.Empty, "command timed out"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await AssertAsync.ThrowsAsync<OperationCanceledException>(() => Create(runner).SearchAsync("tulip", 25, cancellation.Token));
    }

    [TestMethod]
    public async Task Resolve_AsksForOneCombinedStreamOfTheVideoPage()
    {
        FakeRunner runner = new(new(true, "https://rr1.googlevideo.test/videoplayback?x=1\n", string.Empty));

        string url = await Create(runner).ResolvePlaybackUrlAsync(Id);

        Assert.AreEqual("https://rr1.googlevideo.test/videoplayback?x=1", url);
        IReadOnlyList<string> arguments = runner.Calls.Single();
        CollectionAssert.Contains(arguments.ToList(), "-g");
        Assert.AreEqual(YtDlpYouTubeService.PlaybackFormat, arguments[arguments.ToList().IndexOf("-f") + 1]);
        Assert.AreEqual($"https://www.youtube.com/watch?v={Id}", arguments.Last());
    }

    [TestMethod]
    public async Task Resolve_SkipsLinesThatAreNotAddresses()
    {
        FakeRunner runner = new(new(true, "some note\n\nhttps://a.test/one.mp4\nhttps://a.test/two.mp4\n", string.Empty));

        Assert.AreEqual("https://a.test/one.mp4", await Create(runner).ResolvePlaybackUrlAsync(Id));
    }

    [TestMethod]
    public async Task Resolve_OfAnIdThatIsNotOneRunsNothing()
    {
        FakeRunner runner = new(new(true, "https://a.test/x.mp4", string.Empty));

        await AssertAsync.ThrowsAsync<YouTubeException>(() => Create(runner).ResolvePlaybackUrlAsync("--cookies-from-browser"));

        Assert.AreEqual(0, runner.Calls.Count, "an id that is not a video id never reaches the command line");
    }

    [TestMethod]
    public async Task Resolve_WithoutAStreamSaysSo()
    {
        FakeRunner runner = new(new(true, "WARNING: nothing\n", string.Empty));

        YouTubeException exception = await AssertAsync.ThrowsAsync<YouTubeException>(() => Create(runner).ResolvePlaybackUrlAsync(Id));

        Assert.AreEqual("YouTube gave no stream for this video.", exception.Message);
    }

    [TestMethod]
    public async Task Resolve_ReportsTheFailureOfYtDlp()
    {
        FakeRunner runner = new(new(false, string.Empty, "ERROR: Video unavailable\n"));

        YouTubeException exception = await AssertAsync.ThrowsAsync<YouTubeException>(() => Create(runner).ResolvePlaybackUrlAsync(Id));

        Assert.AreEqual("The video could not be played: Video unavailable", exception.Message);
    }
}
