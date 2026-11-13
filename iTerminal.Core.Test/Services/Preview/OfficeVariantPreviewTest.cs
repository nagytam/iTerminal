using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Preview;
using Moq;

namespace iTerminal.Core.Test.Services.Preview;

/// <summary>
/// The preview of every Office variant goes through the same converter as the F3 viewer, so it shows the text rather
/// than a hex dump of the zip or the binary container, whatever the variant and whichever drive it is on.
/// </summary>
[TestClass]
public class OfficeVariantPreviewTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "preview_office_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteTree()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("doc.dotx")]
    [DataRow("doc.dotm")]
    [DataRow("doc.docm")]
    [DataRow("sheet.xltx")]
    [DataRow("sheet.xltm")]
    [DataRow("slides.potx")]
    [DataRow("slides.ppsx")]
    [DataRow("slides.ppsm")]
    [DataRow("text.odt")]
    [DataRow("text.ott")]
    [DataRow("text.fodt")]
    [DataRow("sheet.ods")]
    [DataRow("sheet.fods")]
    [DataRow("slides.odp")]
    [DataRow("slides.fodp")]
    [DataRow("drawing.odg")]
    [DataRow("drawing.otg")]
    [DataRow("legacy.doc")]
    [DataRow("legacy.dot")]
    [DataRow("legacy.xls")]
    [DataRow("legacy.xlt")]
    [DataRow("legacy.ppt")]
    [DataRow("legacy.pot")]
    [DataRow("legacy.pps")]
    [DataRow("legacy.rtf")]
    public async Task OfficeVariant_IsRenderedThroughTheConverter_NotShownAsBytes(string fileName)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00]);
        Mock<IFileToTextConverterService> converter = new();
        converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, model) => model.Lines.Add("rendered by the converter"))
            .Returns(Task.CompletedTask);

        FilePreview preview = await new FilePreviewService(converter.Object)
            .BuildAsync(new FileSystemEntry(path), Mock.Of<IFileSystem>(), 60, CancellationToken.None);

        CollectionAssert.Contains(preview.Lines.ToList(), "rendered by the converter", $"{fileName} was not rendered.");
        converter.Verify(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("doc.dotx")]
    [DataRow("sheet.xltm")]
    [DataRow("slides.ppsm")]
    [DataRow("text.fodt")]
    [DataRow("legacy.doc")]
    public async Task OfficeVariant_OnADriveWithNoLocalPath_IsRenderedFromItsBytes(string fileName)
    {
        // A remote entry is read through the file system, so its bytes must reach the converter even though no local
        // file exists at that path.
        Mock<IFileToTextConverterService> converter = new();
        converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, model) => model.Lines.Add("read over the drive"))
            .Returns(Task.CompletedTask);
        Mock<IMemoryFile> memory = new();
        memory.Setup(m => m.Content).Returns([0x50, 0x4B, 0x03, 0x04]);
        Mock<IFileSystem> drive = new();
        drive.Setup(f => f.ExistsFile(It.IsAny<Url>())).Returns(true);
        drive.Setup(f => f.ReadFileAsync(It.IsAny<Url>())).ReturnsAsync(memory.Object);

        var entry = new VirtualDriveEntry(new Url("smb://host/Share/" + fileName), fileName) { Size = 8 };

        FilePreview preview = await new FilePreviewService(converter.Object)
            .BuildAsync(entry, drive.Object, 60, CancellationToken.None);

        CollectionAssert.Contains(preview.Lines.ToList(), "read over the drive", $"{fileName} was not rendered from the drive.");
    }
}
