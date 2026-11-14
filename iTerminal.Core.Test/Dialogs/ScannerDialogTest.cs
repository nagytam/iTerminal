using iConsole.Interfaces;
using iTerminal.Core.Dialogs.Scanning;
using iTerminal.Core.Services.Scanning;
using iTerminal.Core.Test.Helper;
using Moq;
using SkiaSharp;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class ScannerDialogTest
{
    private static readonly ScannerDevice Office = new("office", "Office Scanner");

    private Mock<IScanService> _service = null!;
    private ScanOptions? _lastScan;
    private ScannerDialog _dialog = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new Mock<IScanService>();
        _service.Setup(s => s.ListScannersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScannerListing(true, [Office], null));
        _service.Setup(s => s.ScanAsync(Office, It.IsAny<ScanOptions>(), It.IsAny<CancellationToken>()))
            .Returns((ScannerDevice _, ScanOptions options, CancellationToken _) =>
            {
                _lastScan = options;
                int pages = options.Source == ScanSource.Feeder ? 2 : 1;
                return Task.FromResult(new ScanResult(Enumerable.Range(0, pages).Select(_ => Page()).ToList(), "scanned"));
            });
        _dialog = new ScannerDialog(_service.Object, UiTestHelper.TestTheme());
        _dialog.Load(Path.GetTempPath());
    }

    /// <summary>A real, decodable page as PNG.</summary>
    private static ScanPage Page()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ScanPage(data.ToArray());
    }

    private async Task OpenAsync()
    {
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.ScannerCountForTest == 1, "the scanner list to be read");
    }

    private Task PressAsync(ConsoleKey key, char keyChar = '\0') =>
        _dialog.HandleKeyEventAsync(UiTestHelper.Key(key, keyChar));

    [TestMethod]
    [Timeout(15_000)]
    public async Task Opening_ListsTheScanners()
    {
        await OpenAsync();

        Assert.AreEqual(1, _dialog.ScannerCountForTest);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Enter_ScansOnePageFromTheFlatbedByDefault()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.Enter);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PageCountForTest == 1, "the page to arrive");

        Assert.IsNotNull(_lastScan);
        Assert.AreEqual(ScanSource.Flatbed, _lastScan.Source);
        Assert.AreEqual(300, _lastScan.Dpi);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task D_SwitchesToTheFeeder_AndTheScanTakesEverySheet()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.D, 'd');
        Assert.AreEqual(ScanSource.Feeder, _dialog.OptionsForTest.Source);

        await PressAsync(ConsoleKey.S, 's');
        await UiTestHelper.WaitUntilAsync(() => _dialog.PageCountForTest == 2, "both sheets to arrive");

        Assert.AreEqual(ScanSource.Feeder, _lastScan!.Source);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Plus_StepsTheResolutionThroughTheListOfResolutions()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.Add, '+');

        Assert.AreEqual(600, _dialog.OptionsForTest.Dpi);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Screen_ShowsTheScannerTheSettingsAndThePageCount()
    {
        await OpenAsync();
        await PressAsync(ConsoleKey.Enter);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PageCountForTest == 1, "the page to arrive");

        _dialog.OnIdle();
        iConsole.Test.Core.ScreenBuffer screen = UiTestHelper.Render(_dialog);

        UiTestHelper.AssertScreenContains(screen, "Office Scanner");
        UiTestHelper.AssertScreenContains(screen, "300 dpi");
        UiTestHelper.AssertScreenContains(screen, "Page 1 of 1");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Save_WithoutALocalFolder_SaysSoAndWritesNothing()
    {
        _dialog.Load(null);
        await OpenAsync();
        await PressAsync(ConsoleKey.Enter);
        await UiTestHelper.WaitUntilAsync(() => _dialog.PageCountForTest == 1, "the page to arrive");

        await PressAsync(ConsoleKey.W, 'w');

        StringAssert.Contains(_dialog.StatusForTest, "local folder");
        _service.Verify(s => s.SaveAsync(It.IsAny<ScanPage>(), It.IsAny<string>(), It.IsAny<ScanFileFormat>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
