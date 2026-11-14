using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Test.Video;
using iTerminal.Core.Video;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Video;

[TestClass]
public class ImageFileViewerTurnAndStepTest
{
    private static readonly VideoInfo Info = new(400, 200, TimeSpan.Zero, 25);

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static ImageFileViewerDialog CreateDialog(string path, IBeepService? beep = null) =>
        new(new FakeVideoDecoderFactory(() => new FakeVideoDecoder(Info, frameCount: 1000)), beep ?? Mock.Of<IBeepService>(), new DateTimeProviderService(), CreateThemeService())
        {
            FilePath = path,
        };

    private static async Task ShowAsync(ImageFileViewerDialog dialog)
    {
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
    }

    /// <summary>Draws, which starts the load, and waits for the picture to arrive.</summary>
    /// <remarks>
    /// Two draws, each awaited: the first may only record the room (before layout it draws nothing), and the load that the
    /// second one starts must finish before the picture is read. After a turn the old picture stays up until the new one lands.
    /// </remarks>
    private static async Task DrawUntilLoadedAsync(ImageFileViewerDialog dialog)
    {
        var buffer = new iConsole.Test.Core.MockBuffer(new Size(120, 40));
        dialog.Draw(buffer);
        await dialog.LoadTask;
        dialog.Draw(buffer);
        await dialog.LoadTask;
        buffer = new iConsole.Test.Core.MockBuffer(new Size(120, 40));
        dialog.Draw(buffer);
    }

    private static KeyEvent Key(ConsoleKey key, char keyChar = '\0') => new(keyChar, key, false, false, false);

    /// <summary>A folder with three pictures; the names are what matters, the viewer reads only their extensions here.</summary>
    private static string[] CreateFolder(out string folder)
    {
        string created = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        folder = created;
        Directory.CreateDirectory(created);
        string[] names = ["a.png", "b.png", "c.png"];
        foreach (string name in names)
        {
            File.WriteAllBytes(Path.Combine(created, name), []);
        }

        return names.Select(n => Path.Combine(created, n)).ToArray();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RotateClockwise_TurnsThePictureToStandUpright()
    {
        var dialog = CreateDialog("/pictures/photo.png");
        await ShowAsync(dialog);
        await DrawUntilLoadedAsync(dialog);
        var before = dialog.Picture!;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.R, 'r'));
        await DrawUntilLoadedAsync(dialog);

        var after = dialog.Picture!;
        Assert.IsTrue(after.Columns < after.Rows, "a wide picture turned a quarter is tall");
        Assert.IsTrue(before.Columns > before.Rows);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TwoQuarterTurns_ReturnToTheUnturnedShape()
    {
        var dialog = CreateDialog("/pictures/photo.png");
        await ShowAsync(dialog);
        await DrawUntilLoadedAsync(dialog);
        var before = dialog.Picture!;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.R, 'r'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.R, 'r'));
        await DrawUntilLoadedAsync(dialog);

        Assert.AreEqual(before.Columns, dialog.Picture!.Columns);
        Assert.AreEqual(before.Rows, dialog.Picture.Rows);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Mirror_KeepsTheShapeAndRedrawsThePicture()
    {
        var dialog = CreateDialog("/pictures/photo.png");
        await ShowAsync(dialog);
        await DrawUntilLoadedAsync(dialog);
        var before = dialog.Picture!;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.H, 'h'));
        await DrawUntilLoadedAsync(dialog);

        Assert.AreEqual(before.Columns, dialog.Picture!.Columns);
        Assert.AreEqual(before.Rows, dialog.Picture.Rows);
        CollectionAssert.AreNotEqual(before.Rgb, dialog.Picture.Rgb, "the gradient runs the other way");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightArrow_ShowsTheNextPictureInTheFolderAndWraps()
    {
        string[] pictures = CreateFolder(out string folder);
        try
        {
            var dialog = CreateDialog(pictures[0]);
            await ShowAsync(dialog);
            await DrawUntilLoadedAsync(dialog);

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
            Assert.AreEqual(pictures[1], dialog.FilePath);
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
            Assert.AreEqual(pictures[2], dialog.FilePath);
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
            Assert.AreEqual(pictures[0], dialog.FilePath, "the last picture wraps to the first");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftArrow_ShowsThePreviousPicture()
    {
        string[] pictures = CreateFolder(out string folder);
        try
        {
            var dialog = CreateDialog(pictures[0]);
            await ShowAsync(dialog);
            await DrawUntilLoadedAsync(dialog);

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
            Assert.AreEqual(pictures[2], dialog.FilePath, "the first picture wraps to the last");
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
            Assert.AreEqual(pictures[1], dialog.FilePath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NextPicture_LoadsTheNewPictureAndResetsTheTurn()
    {
        string[] pictures = CreateFolder(out string folder);
        try
        {
            var dialog = CreateDialog(pictures[0]);
            await ShowAsync(dialog);
            await DrawUntilLoadedAsync(dialog);
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.R, 'r'));
            await DrawUntilLoadedAsync(dialog);

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
            await DrawUntilLoadedAsync(dialog);

            Assert.AreEqual(pictures[1], dialog.FilePath);
            Assert.IsTrue(dialog.Picture!.Columns > dialog.Picture.Rows, "the next picture is shown the way it is on disk");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APictureAloneInItsFolder_Beeps()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "only.png");
        File.WriteAllBytes(path, []);
        try
        {
            var beep = new Mock<IBeepService>();
            var dialog = CreateDialog(path, beep: beep.Object);
            await ShowAsync(dialog);

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

            Assert.AreEqual(path, dialog.FilePath);
            beep.Verify(b => b.Beep(), Times.Once);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
