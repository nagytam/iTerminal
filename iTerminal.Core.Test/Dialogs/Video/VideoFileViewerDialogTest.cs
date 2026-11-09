using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Test.Video;
using iTerminal.Core.Video;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Video;

[TestClass]
public class VideoFileViewerDialogTest
{
    private static readonly VideoInfo Info = new(160, 90, TimeSpan.FromSeconds(90), 25);

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static VideoFileViewerDialog CreateDialog(bool available = true, Func<IVideoDecoder>? create = null) =>
        new(new FakeVideoDecoderFactory(create ?? (() => new FakeVideoDecoder(Info)), available), CreateThemeService())
        {
            FilePath = "/videos/clip.mp4",
        };

    private static async Task<Control> ShowAsync(VideoFileViewerDialog dialog)
    {
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        await dialog.OpenTask;
        return root;
    }

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

    private static string Render(VideoFileViewerDialog dialog)
    {
        var buffer = new iConsole.Test.Core.MockBuffer(new Size(120, 40));
        dialog.Draw(buffer);
        return buffer.ContentAsString;
    }

    private static KeyEvent Key(char keyChar, ConsoleKey consoleKey, bool shift = false) =>
        new(keyChar, consoleKey, shift, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Show_StartsPlayingAndDrawsAsciiArtWithTheTransport()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.Player?.Frame is not null, "the first frame");
            await WaitForAsync(() => dialog.State == VideoPlaybackState.Playing, "playing");

            var text = Render(dialog);

            StringAssert.Contains(text, "clip.mp4");
            StringAssert.Contains(text, "160×90");
            StringAssert.Contains(text, "Pause");
            StringAssert.Contains(text, "Stop");
            StringAssert.Contains(text, "10s >>");
            StringAssert.Contains(text, "Colour: off");
            StringAssert.Contains(text, VideoFileViewerDialog.KeyHints.Trim());
            // The picture itself: the fake's gradient uses the dense end of the ramp.
            Assert.IsTrue(text.Contains('$') || text.Contains('@'), "the frame should be drawn with the ramp's dense characters");
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTransportButtonsFollowTheChosenLanguage()
    {
        iConsole.Localization.Localizer localizer = new([new iConsole.Localization.ResourceLocalizationSource(
            new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(VideoFileViewerDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage("de"));
        using IDisposable language = iConsole.Localization.UiText.Scope(localizer);
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.State == VideoPlaybackState.Playing, "playing");

            var text = Render(dialog);

            StringAssert.Contains(text, "Farbe: aus");
            StringAssert.Contains(text, "Stopp");
            StringAssert.Contains(text, "10 s >>");
            Assert.IsFalse(text.Contains("Colour: off", StringComparison.Ordinal), text);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheInformationHeaderFollowsEveryChosenLanguage()
    {
        foreach (string code in new[] { "de", "fr", "es", "it", "hu" })
        {
            iConsole.Localization.Localizer localizer = new([new iConsole.Localization.ResourceLocalizationSource(
                new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(VideoFileViewerDialog).Assembly))]);
            Assert.IsTrue(localizer.SetLanguage(code));
            using IDisposable language = iConsole.Localization.UiText.Scope(localizer);

            Assert.IsTrue(localizer.TryGet("Video.Info.SourceSize", out string sourceSize));
            Assert.IsTrue(localizer.TryGet("Video.Info.ShownAsCharacters", out string shownAs));
            if (code != "fr")
            {
                Assert.AreNotEqual("{0}×{1} pixels", sourceSize, code);
            }
            Assert.AreNotEqual("shown as {0}×{1} characters", shownAs, code);

            VideoFileViewerDialog dialog = CreateDialog();
            Control root = await ShowAsync(dialog);
            try
            {
                await WaitForAsync(() => dialog.Player?.Frame is not null, "the first frame");
                VideoFrame frame = dialog.Player!.Frame!;
                string sizeText = localizer.Get("Video.Info.SourceSize", Info.Width, Info.Height);
                string shownAsText = localizer.Get("Video.Info.ShownAsCharacters", frame.Columns, frame.Rows);
                iConsole.Test.Core.ScreenBuffer screen = iConsole.Test.Core.ScreenBuffer.Render(root, 120, 40);

                Assert.IsTrue(screen.Contains(sizeText), $"{code}: {screen}");
                Assert.IsTrue(screen.Contains("fps"), $"{code}: {screen}");
                Assert.IsTrue(screen.Contains(shownAsText), $"{code}: {screen}");
            }
            finally
            {
                dialog.Close();
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Show_FitsThePictureToTheRoomKeepingItsShape()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.Player?.Frame is not null, "the first frame");
            Render(dialog);
            await WaitForAsync(() => dialog.Player!.Frame is { } f && f.Columns > 0, "a frame");

            var frame = dialog.Player!.Frame!;
            Assert.IsTrue(Math.Abs(frame.Columns / (double)frame.Rows - 2 * Info.AspectRatio) < 0.6,
                $"{frame.Columns}x{frame.Rows} should keep the 16:9 shape on cells twice as tall as wide");
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SpaceTogglesPlayPause()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.State == VideoPlaybackState.Playing, "playing");

            Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar)));
            Assert.AreEqual(VideoPlaybackState.Paused, dialog.State);
            StringAssert.Contains(Render(dialog), "Play");

            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));
            Assert.AreEqual(VideoPlaybackState.Playing, dialog.State);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowsSeekAndShiftSeeksFurther()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.Player?.Frame is not null, "the first frame");
            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar)); // pause so the clock holds still
            await WaitForAsync(() => dialog.State == VideoPlaybackState.Paused, "paused");
            dialog.Player!.SeekTo(TimeSpan.FromSeconds(20));
            await WaitForAsync(() => dialog.Player.Frame!.Time >= TimeSpan.FromSeconds(20), "the seek");

            await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.RightArrow));
            Assert.AreEqual(25, dialog.Player.Position.TotalSeconds, 0.01);

            await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.LeftArrow, shift: true));
            Assert.AreEqual(0, dialog.Player.Position.TotalSeconds, 0.01);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SAndHomeStopAndRestart()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.State == VideoPlaybackState.Playing, "playing");

            await dialog.HandleKeyEventAsync(Key('s', ConsoleKey.S));
            Assert.AreEqual(VideoPlaybackState.Stopped, dialog.State);
            Assert.AreEqual(TimeSpan.Zero, dialog.Player!.Position);

            await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.Home));
            Assert.AreEqual(VideoPlaybackState.Playing, dialog.State);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CTogglesColour()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            Assert.AreEqual(AsciiColorMode.Mono, dialog.ColorMode);

            await dialog.HandleKeyEventAsync(Key('c', ConsoleKey.C));

            Assert.AreEqual(AsciiColorMode.Color, dialog.ColorMode);
            StringAssert.Contains(Render(dialog), "Colour: on");
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheButtonsDriveThePlayer()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);
        try
        {
            await WaitForAsync(() => dialog.State == VideoPlaybackState.Playing, "playing");
            Button play = dialog.TransportButtons[2];
            Button stop = dialog.TransportButtons[3];

            await RunAsync(play);
            Assert.AreEqual(VideoPlaybackState.Paused, dialog.State);

            await RunAsync(stop);
            Assert.AreEqual(VideoPlaybackState.Stopped, dialog.State);

            await RunAsync(dialog.TransportButtons[4]); // forward ten seconds
            Assert.AreEqual(10, dialog.Player!.Position.TotalSeconds, 0.01);
        }
        finally
        {
            dialog.Close();
        }

        static async Task RunAsync(Button button) => await button.Commands.First().RunAsync(null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingStopsThePlayerThread()
    {
        var decoder = new FakeVideoDecoder(Info);
        var dialog = CreateDialog(create: () => decoder);
        await ShowAsync(dialog);
        await WaitForAsync(() => dialog.Player?.Frame is not null, "the first frame");

        dialog.Close();

        Assert.IsNull(dialog.Player);
        Assert.ThrowsExactly<ObjectDisposedException>(() => decoder.Open(TimeSpan.Zero, 1, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutFfmpegTheDialogSaysSo()
    {
        var dialog = CreateDialog(available: false);
        await ShowAsync(dialog);
        try
        {
            StringAssert.Contains(Render(dialog), "needs ffmpeg");
            Assert.IsNull(dialog.Player);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatCannotBeOpenedShowsTheReason()
    {
        var dialog = CreateDialog(create: () => throw new InvalidOperationException("The file has no video stream."));
        await ShowAsync(dialog);
        try
        {
            StringAssert.Contains(Render(dialog), "The file has no video stream.");
        }
        finally
        {
            dialog.Close();
        }
    }
}
