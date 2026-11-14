using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Dialogs.YouTube;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.YouTube;
using iTerminal.Core.Video;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.YouTube;

[TestClass]
[DoNotParallelize]
public class YouTubeDialogTest
{
    private static readonly YouTubeVideo Tulip = new("abcdefghijk", "Tulips in spring", "Garden channel", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(5), 2500, new DateTime(2026, 4, 1));
    private static readonly YouTubeVideo Other = new("lmnopqrstuv", "A second video", "Other channel", null, null, null);

    /// <summary>Answers from fixed lists; a search or a stream lookup can be made to fail.</summary>
    private sealed class FakeYouTube : IYouTubeService
    {
        public IReadOnlyList<YouTubeVideo> Videos { get; set; } = [Tulip, Other];
        public string Stream { get; set; } = "https://example.test/stream.mp4";
        public Exception? Failure { get; set; }
        public Exception? ResolveFailure { get; set; }
        public List<string> Searches { get; } = [];
        public List<string> Resolved { get; } = [];

        public Task<IReadOnlyList<YouTubeVideo>> SearchAsync(string query, int count, CancellationToken cancellationToken = default)
        {
            Searches.Add(query);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Videos);
        }

        public Task<string> ResolvePlaybackUrlAsync(string videoId, CancellationToken cancellationToken = default)
        {
            Resolved.Add(videoId);
            if (ResolveFailure is not null)
            {
                throw ResolveFailure;
            }

            return Task.FromResult(Stream);
        }
    }

    private sealed record Harness(YouTubeDialog Dialog, FakeYouTube Service, Mock<IFileLauncher> Launcher, List<VideoFileViewerDialog> Shown, VideoFileViewerDialog Player);

    private static Harness Create(FakeYouTube? service = null)
    {
        service ??= new FakeYouTube();
        Mock<IFileLauncher> launcher = new();
        launcher.Setup(l => l.OpenUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(LaunchOutcome.Success);
        Mock<IVideoDecoderFactory> decoders = new();
        VideoFileViewerDialog player = new(decoders.Object, TestTheme());
        YouTubeDialog dialog = new(service, launcher.Object, player, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        // The running app shows the viewer as a dialog; here the hand-off is recorded and the viewer is not opened.
        List<VideoFileViewerDialog> shown = [];
        dialog.ShowPlayer = viewer =>
        {
            shown.Add(viewer);
            return Task.CompletedTask;
        };
        return new Harness(dialog, service, launcher, shown, player);
    }

    /// <summary>Lets the request in flight finish and applies what it left, as the idle pass does in the running app.</summary>
    private static async Task SettleAsync(YouTubeDialog dialog)
    {
        await dialog.RequestTask;
        dialog.ApplyPendingForTest();
    }

    private static async Task SearchAsync(Harness h, string term)
    {
        h.Dialog.QueryBox.Text = term;
        h.Dialog.StartSearch();
        await SettleAsync(h.Dialog);
    }

    [TestMethod]
    public async Task ASearchFillsTheResultsAndShowsTheFirstVideosDetails()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        await SearchAsync(h, "tulip");

        Assert.AreEqual("tulip", h.Service.Searches.Single());
        Assert.AreEqual(2, h.Dialog.Results.ListModel.ListItems.Count);
        Assert.AreEqual("2 videos found.", h.Dialog.Status);
        string screen = Render(h.Dialog).ToString()!;
        StringAssert.Contains(screen, "Videos (2)");
        StringAssert.Contains(screen, "Tulips in spring");
        StringAssert.Contains(screen, "Channel: Garden channel");
        StringAssert.Contains(screen, "Duration: 3:05");
        StringAssert.Contains(screen, "Views: " + 2500.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
        StringAssert.Contains(screen, "Published: 2026-04-01");
    }

    [TestMethod]
    public async Task AnEmptyTermIsNotSearchedFor()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        h.Dialog.QueryBox.Text = "   ";
        h.Dialog.StartSearch();

        Assert.AreEqual(0, h.Service.Searches.Count);
        Assert.AreEqual("Type something to search for.", h.Dialog.Status);
    }

    [TestMethod]
    public async Task NothingFoundSaysSo()
    {
        FakeYouTube service = new() { Videos = [] };
        Harness h = Create(service);
        await ShowAsync(h.Dialog);

        await SearchAsync(h, "zzzz");

        Assert.AreEqual("Nothing found for \"zzzz\".", h.Dialog.Status);
    }

    [TestMethod]
    public async Task EnterOnAVideoResolvesItsStreamAndHandsItToTheViewer()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        h.Dialog.Play();
        await SettleAsync(h.Dialog);
        await Task.Yield();

        Assert.AreEqual("abcdefghijk", h.Service.Resolved.Single());
        VideoFileViewerDialog viewer = h.Shown.Single();
        Assert.AreSame(h.Player, viewer);
        Assert.AreEqual("https://example.test/stream.mp4", viewer.FilePath, "the viewer decodes the stream address, as it decodes a file");
        Assert.AreEqual("Tulips in spring", viewer.DisplayName);
        Assert.AreEqual("Playing Tulips in spring.", h.Dialog.Status);
    }

    [TestMethod]
    public async Task PlayWithNoVideoSaysSo()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        h.Dialog.Play();

        Assert.AreEqual(0, h.Service.Resolved.Count);
        Assert.AreEqual("There is no video to play yet. Search first.", h.Dialog.Status);
    }

    [TestMethod]
    public async Task AFailedStreamLookupShowsItsMessageAndOpensNothing()
    {
        FakeYouTube service = new() { ResolveFailure = new YouTubeException("The video could not be played: Video unavailable") };
        Harness h = Create(service);
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        h.Dialog.Play();
        await SettleAsync(h.Dialog);

        Assert.AreEqual("The video could not be played: Video unavailable", h.Dialog.Status);
        Assert.AreEqual(0, h.Shown.Count);
    }

    [TestMethod]
    public async Task F2OpensTheVideosPageInTheBrowser()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        h.Dialog.OpenInBrowser();
        await h.Dialog.RequestTask;
        h.Dialog.ApplyPendingForTest();

        h.Launcher.Verify(l => l.OpenUrlAsync("https://www.youtube.com/watch?v=abcdefghijk", It.IsAny<CancellationToken>()), Times.Once);
        StringAssert.StartsWith(h.Dialog.Status, "Opened https://www.youtube.com/watch?v=abcdefghijk in the browser.");
    }

    [TestMethod]
    [DataRow(75, "1:15")]
    [DataRow(3599, "59:59")]
    [DataRow(3725, "1:02:05")]
    public void FormatDuration_ShowsMinutesAndHoursTheWayPeopleRead_Them(int seconds, string expected) =>
        Assert.AreEqual(expected, YouTubeDialog.FormatDuration(TimeSpan.FromSeconds(seconds)));
}
