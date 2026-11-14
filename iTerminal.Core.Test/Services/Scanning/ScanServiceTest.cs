using iTerminal.Core.Services.Scanning;
using Moq;

namespace iTerminal.Core.Test.Services.Scanning;

[TestClass]
public class ScanServiceTest
{
    private string _folder = null!;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-scan-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>A real, decodable page: a 10 by 10 white picture, as PNG.</summary>
    private static ScanPage Page()
    {
        using var bitmap = new SkiaSharp.SKBitmap(10, 10);
        bitmap.Erase(SkiaSharp.SKColors.White);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return new ScanPage(data.ToArray());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ScanAsync_PassesTheRequestToTheBackend()
    {
        var backend = new Mock<IScannerBackend>();
        var device = new ScannerDevice("id", "Scanner");
        var options = new ScanOptions(300, ScanColorMode.Color, ScanSource.Feeder);
        backend.Setup(b => b.ScanAsync(device, options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResult([Page(), Page()], "two pages"));
        var service = new ScanService(backend.Object);

        ScanResult result = await service.ScanAsync(device, options);

        Assert.IsTrue(result.Success);
        Assert.HasCount(2, result.Pages);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SaveAsync_WritesThePageInTheChosenFormat()
    {
        var service = new ScanService(new Mock<IScannerBackend>().Object);
        string path = Path.Combine(_folder, "scan.jpg");

        SaveResult result = await service.SaveAsync(Page(), path, ScanFileFormat.Jpeg);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Saved scan.jpg.", result.Message);
        byte[] written = await File.ReadAllBytesAsync(path);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8 }, written[..2]);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SaveAsync_ReportsAFolderThatIsNotThere_WithoutThrowing()
    {
        var service = new ScanService(new Mock<IScannerBackend>().Object);

        SaveResult result = await service.SaveAsync(Page(), Path.Combine(_folder, "missing", "scan.png"), ScanFileFormat.Png);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "missing");
    }
}
