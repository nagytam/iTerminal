using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Graphics;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Test.Video;
using iTerminal.Core.Video;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Video;

/// <summary>The terminal capability is process-wide, so these run one at a time.</summary>
[TestClass]
[DoNotParallelize]
public class ImageFileViewerDialogSixelTest
{
    private static readonly VideoInfo Info = new(400, 200, TimeSpan.Zero, 25);

    private TerminalGraphics? _saved;

    [TestInitialize]
    public void Save() => _saved = TerminalGraphics.Current;

    [TestCleanup]
    public void Restore() => TerminalGraphics.Current = _saved!;

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static async Task<(ImageFileViewerDialog Dialog, SixelOverlay Overlay, FakeVideoDecoder Decoder)> OpenAsync()
    {
        var overlay = new SixelOverlay();
        var decoder = new FakeVideoDecoder(Info, frameCount: 1);
        var dialog = new ImageFileViewerDialog(
            new FakeVideoDecoderFactory(() => decoder),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            CreateThemeService(),
            overlay)
        {
            FilePath = "/pictures/photo.png",
        };
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        return (dialog, overlay, decoder);
    }

    private static async Task<string> DrawUntilLoadedAsync(ImageFileViewerDialog dialog)
    {
        dialog.Draw(new iConsole.Test.Core.MockBuffer(new Size(120, 40)));
        await dialog.LoadTask;
        var buffer = new iConsole.Test.Core.MockBuffer(new Size(120, 40));
        dialog.Draw(buffer);
        return buffer.ContentAsString;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnASixelTerminal_ThePictureIsABitmapSizedInPixels()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var (dialog, overlay, decoder) = await OpenAsync();

        var text = await DrawUntilLoadedAsync(dialog);

        Assert.IsTrue(dialog.IsShownAsSixel);
        Assert.IsNotNull(overlay.Current);
        StringAssert.StartsWith(overlay.Current.Data, "\u001bP");
        StringAssert.Contains(text, "400×200 pixels");
        StringAssert.Contains(text, "shown as");
        Assert.IsFalse(text.Contains("characters"), "no ASCII art on a Sixel terminal");
        var open = decoder.Opens.Last();
        Assert.AreEqual(2.0, (double)open.Columns / open.Rows, 0.02, "the pixels keep the 2:1 shape of the image");
        Assert.AreEqual(open.Columns, dialog.Picture!.Columns);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheBitmapSitsInsideTheRoomTheDialogHas()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var (dialog, overlay, _) = await OpenAsync();

        await DrawUntilLoadedAsync(dialog);

        var picture = overlay.Current!;
        Assert.IsTrue(picture.X >= dialog.AbsolutePosition.X + 1);
        Assert.IsTrue(picture.X + picture.Columns <= dialog.AbsolutePosition.X + dialog.ViewSize.Width - 1);
        Assert.IsTrue(picture.Y + picture.Rows <= dialog.AbsolutePosition.Y + dialog.ViewSize.Height - 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowingTheMetadata_WithdrawsTheBitmap()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var (dialog, overlay, _) = await OpenAsync();
        await DrawUntilLoadedAsync(dialog);
        Assert.IsNotNull(overlay.Current);

        await dialog.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, false, false, false));
        dialog.Draw(new iConsole.Test.Core.MockBuffer(new Size(120, 40)));

        Assert.IsNull(overlay.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheDialog_WithdrawsTheBitmap()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var (dialog, overlay, _) = await OpenAsync();
        await DrawUntilLoadedAsync(dialog);

        dialog.Close();

        Assert.IsNull(overlay.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutSixel_ThePictureIsAsciiArtAsBefore()
    {
        TerminalGraphics.Current = new TerminalGraphics(false);
        var (dialog, overlay, _) = await OpenAsync();

        var text = await DrawUntilLoadedAsync(dialog);

        Assert.IsFalse(dialog.IsShownAsSixel);
        Assert.IsNull(overlay.Current);
        StringAssert.Contains(text, "characters");
    }
}
