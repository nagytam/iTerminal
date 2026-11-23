using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Dialogs.Video;
using iConsole.Test.Core.Video;
using iConsole.Media.Video;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Video;

[TestClass]
public class ImageFileViewerDialogTest
{
    private static readonly VideoInfo Info = new(400, 200, TimeSpan.Zero, 25);

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static ImageFileViewerDialog CreateDialog(bool available = true, Func<IVideoDecoder>? create = null) =>
        new(new FakeVideoDecoderFactory(create ?? (() => new FakeVideoDecoder(Info, frameCount: 1)), available), Mock.Of<IBeepService>(), new DateTimeProviderService(), CreateThemeService())
        {
            FilePath = "/pictures/photo.png",
        };

    private static async Task<Control> ShowAsync(ImageFileViewerDialog dialog)
    {
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        return root;
    }

    /// <summary>Draws (which is what starts the load) and waits for the picture.</summary>
    private static async Task<string> DrawUntilLoadedAsync(ImageFileViewerDialog dialog)
    {
        var buffer = new iConsole.Test.Core.ScreenBuffer(120, 40);
        try
        {
            dialog.Draw(buffer);
            await dialog.LoadTask;
            buffer = new iConsole.Test.Core.ScreenBuffer(120, 40);
            dialog.Draw(buffer);
        }
        catch (Exception exception)
        {
            // Drawn on a ScreenBuffer, the buffer the testing rules ask for. Diagnostics for an intermittent NullReferenceException seen only on the Windows CI runner: the exception
            // with its full stack trace, and the state the dialog was in. Printed to the test output, then rethrown.
            Console.WriteLine("DIAG ImageFileViewerDialogTest: draw threw " + exception.GetType().FullName);
            Console.WriteLine("DIAG exception: " + exception);
            Console.WriteLine($"DIAG thread={Environment.CurrentManagedThreadId} os={Environment.OSVersion} culture={System.Globalization.CultureInfo.CurrentCulture.Name}");
            Console.WriteLine($"DIAG FilePath='{dialog.FilePath}' Error='{dialog.Error}' Picture={(dialog.Picture is null ? "null" : "set")} ColorMode={dialog.ColorMode}");
            Console.WriteLine($"DIAG LoadTask status={dialog.LoadTask.Status} faulted={dialog.LoadTask.IsFaulted} canceled={dialog.LoadTask.IsCanceled}");
            Console.WriteLine($"DIAG ViewSize={dialog.ViewSize} IsVisible={dialog.IsVisible} Parent={(dialog.Parent is null ? "null" : dialog.Parent.GetType().Name)}");
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        return string.Join(Environment.NewLine, buffer.Lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Draw_LoadsThePictureAndShowsItAsAscii()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);

        var text = await DrawUntilLoadedAsync(dialog);

        StringAssert.Contains(text, "photo.png");
        StringAssert.Contains(text, "400×200 pixels");
        StringAssert.Contains(text, "characters");
        StringAssert.Contains(text, "Colour: on");
        Assert.IsNotNull(dialog.Picture);
        Assert.IsTrue(text.Contains('$') || text.Contains('@'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Picture_KeepsTheShapeOfTheImage()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);

        await DrawUntilLoadedAsync(dialog);

        var picture = dialog.Picture!;
        Assert.AreEqual(2 * 2 * picture.Rows, picture.Columns, 2, "a 2:1 picture on cells twice as tall as wide");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Drawing_DoesNotReloadWhileTheRoomIsUnchanged()
    {
        var created = 0;
        var dialog = CreateDialog(create: () =>
        {
            Interlocked.Increment(ref created);
            return new FakeVideoDecoder(Info, frameCount: 1);
        });
        await ShowAsync(dialog);

        await DrawUntilLoadedAsync(dialog);
        await DrawUntilLoadedAsync(dialog);

        Assert.AreEqual(1, created);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CTogglesColour()
    {
        var dialog = CreateDialog();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('c', ConsoleKey.C, false, false, false));

        Assert.AreEqual(AsciiColorMode.Mono, dialog.ColorMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutFfmpegTheDialogSaysSo()
    {
        var dialog = CreateDialog(available: false);
        await ShowAsync(dialog);

        var text = await DrawUntilLoadedAsync(dialog);

        StringAssert.Contains(text, "needs ffmpeg");
        Assert.IsNull(dialog.Picture);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADecoderThatDeliversNothingShowsAnError()
    {
        var dialog = CreateDialog(create: () => new FakeVideoDecoder(Info, frameCount: 0));
        await ShowAsync(dialog);

        var text = await DrawUntilLoadedAsync(dialog);

        StringAssert.Contains(text, "No picture could be decoded");
    }
}
