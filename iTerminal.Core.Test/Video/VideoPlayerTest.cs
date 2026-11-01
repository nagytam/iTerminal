using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class VideoPlayerTest
{
    private static readonly VideoInfo Info = new(160, 90, TimeSpan.FromSeconds(40), 25);

    private static async Task WaitForAsync(Func<bool> condition, string what, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Start_ShowsTheFirstPictureWithoutPlaying()
    {
        var decoder = new FakeVideoDecoder(Info);
        using var player = new VideoPlayer(decoder);

        player.Start(16, 4);

        await WaitForAsync(() => player.Frame is not null, "the first frame");
        Assert.AreEqual(VideoPlaybackState.Stopped, player.State);
        Assert.AreEqual(16, player.Frame!.Columns);
        Assert.AreEqual(4, player.Frame.Rows);
        Assert.AreEqual(16 * 4 * 3, player.Frame.Rgb.Length);

        // Stopped means no more pictures: the thread read one and is waiting.
        var version = player.FrameVersion;
        await Task.Delay(150);
        Assert.AreEqual(version, player.FrameVersion);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Play_AdvancesFramesInStepWithTheClock()
    {
        var decoder = new FakeVideoDecoder(Info);
        using var player = new VideoPlayer(decoder);
        player.Start(16, 4);
        await WaitForAsync(() => player.Frame is not null, "the first frame");

        player.Play();

        await WaitForAsync(() => player.Frame!.Time >= TimeSpan.FromMilliseconds(400), "frames to advance");
        Assert.AreEqual(VideoPlaybackState.Playing, player.State);
        // A frame is never shown ahead of the clock (a little slack for the read between the two).
        Assert.IsTrue(player.Frame!.Time <= player.Position + TimeSpan.FromMilliseconds(100));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Pause_FreezesPositionAndPicture()
    {
        using var player = new VideoPlayer(new FakeVideoDecoder(Info));
        player.Start(16, 4);
        player.Play();
        await WaitForAsync(() => player.Frame is { } f && f.Time >= TimeSpan.FromMilliseconds(200), "some playback");

        player.Pause();
        await Task.Delay(100);
        var position = player.Position;
        var version = player.FrameVersion;
        await Task.Delay(300);

        Assert.AreEqual(VideoPlaybackState.Paused, player.State);
        Assert.AreEqual(position, player.Position);
        Assert.AreEqual(version, player.FrameVersion);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Seek_ReopensTheDecoderAtTheTargetAndShowsThatPictureWhilePaused()
    {
        var decoder = new FakeVideoDecoder(Info);
        using var player = new VideoPlayer(decoder);
        player.Start(16, 4);
        await WaitForAsync(() => player.Frame is not null, "the first frame");

        player.SeekTo(TimeSpan.FromSeconds(10));

        await WaitForAsync(() => player.Frame!.Time >= TimeSpan.FromSeconds(10), "the picture at ten seconds");
        Assert.AreEqual(TimeSpan.FromSeconds(10), decoder.Opens[^1].Offset);
        Assert.AreEqual(TimeSpan.FromSeconds(10), player.Position);
        Assert.AreEqual(VideoPlaybackState.Stopped, player.State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Seek_ClampsToTheVideo()
    {
        using var player = new VideoPlayer(new FakeVideoDecoder(Info));
        player.Start(16, 4);
        await WaitForAsync(() => player.Frame is not null, "the first frame");

        player.SeekTo(TimeSpan.FromMinutes(5));
        Assert.IsTrue(player.Position < Info.Duration && player.Position > Info.Duration - TimeSpan.FromSeconds(1));

        player.SeekTo(TimeSpan.FromSeconds(-9));
        Assert.AreEqual(TimeSpan.Zero, player.Position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Resize_RemakesTheFramesAtTheNewSizeFromTheSamePosition()
    {
        var decoder = new FakeVideoDecoder(Info);
        using var player = new VideoPlayer(decoder);
        player.Start(16, 4);
        await WaitForAsync(() => player.Frame is not null, "the first frame");
        player.SeekTo(TimeSpan.FromSeconds(3));
        await WaitForAsync(() => player.Frame!.Time >= TimeSpan.FromSeconds(3), "the seek");

        player.Resize(32, 8);

        await WaitForAsync(() => player.Frame!.Columns == 32, "the new size");
        Assert.AreEqual(8, player.Frame!.Rows);
        Assert.AreEqual((TimeSpan.FromSeconds(3), 32, 8), decoder.Opens[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Resize_ToTheSameSizeDoesNotReopen()
    {
        var decoder = new FakeVideoDecoder(Info);
        using var player = new VideoPlayer(decoder);
        player.Start(16, 4);
        await WaitForAsync(() => player.Frame is not null, "the first frame");
        var opens = decoder.Opens.Count;

        player.Resize(16, 4);
        await Task.Delay(100);

        Assert.AreEqual(opens, decoder.Opens.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EndOfStream_StopsAndGoesBackToTheFirstPicture()
    {
        var decoder = new FakeVideoDecoder(Info, frameCount: 5);
        using var player = new VideoPlayer(decoder);
        player.Start(16, 4);
        player.Play();

        await WaitForAsync(() => player.State == VideoPlaybackState.Stopped && decoder.Opens.Count >= 2, "the video to end");

        Assert.AreEqual(TimeSpan.Zero, decoder.Opens[^1].Offset);
        Assert.AreEqual(TimeSpan.Zero, player.Position);
        Assert.AreEqual(string.Empty, player.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADecoderThatNeverDeliversIsReportedAndNotRetried()
    {
        var decoder = new FakeVideoDecoder(Info, frameCount: 0);
        using var player = new VideoPlayer(decoder);

        player.Start(16, 4);

        await WaitForAsync(() => player.Error.Length > 0, "the error");
        await Task.Delay(150);
        Assert.AreEqual(1, decoder.Opens.Count, "a file that cannot be decoded must not be reopened in a loop");
        Assert.IsNull(player.Frame);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dispose_DisposesTheDecoderAndIsRepeatable()
    {
        var decoder = new FakeVideoDecoder(Info);
        var player = new VideoPlayer(decoder);
        player.Start(8, 2);

        player.Dispose();
        player.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => decoder.Open(TimeSpan.Zero, 1, 1));
    }
}
