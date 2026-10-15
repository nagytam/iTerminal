using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;
using System.Text;

namespace iConsole.Test.Services;

[TestClass]
public class FileToTextConverterServiceTest
{
    [TestMethod]
    public async Task ReadFileAsync_WithNullUrl_ClearsLines()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        textModel.Lines.Add("existing line");
        var mockFileSystem = new Mock<IFileSystem>();

        await service.ReadFileAsync(null, mockFileSystem.Object, textModel);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    [TestMethod]
    public async Task ReadFileAsync_WithNonExistentFile_ClearsLines()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        textModel.Lines.Add("existing line");
        var url = new Url("file:///nonexistent.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(false);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    [TestMethod]
    public async Task ReadFileAsync_WithTextFile_ReadsContent()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///test.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var mockMemoryFile = new Mock<IMemoryFile>();
        var content = Encoding.UTF8.GetBytes("Line 1\nLine 2\nLine 3");
        mockMemoryFile.Setup(mf => mf.Content).Returns(content);

        mockFileSystem.Setup(fs => fs.ReadFileAsync(url))
            .ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.IsTrue(textModel.Lines.Count >= 3);
        Assert.IsTrue(textModel.Lines.Contains("Line 1"));
        Assert.IsTrue(textModel.Lines.Contains("Line 2"));
        Assert.IsTrue(textModel.Lines.Contains("Line 3"));
    }

    [TestMethod]
    public async Task ReadFileAsync_WithEmptyFile_ReadsEmptyContent()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///empty.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Array.Empty<byte>());

        mockFileSystem.Setup(fs => fs.ReadFileAsync(url))
            .ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.AreEqual(1, textModel.Lines.Count);
        Assert.AreEqual(string.Empty, textModel.Lines[0]);
    }

    [TestMethod]
    public async Task ReadFileAsync_ClearsExistingLines()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        textModel.Lines.Add("old line 1");
        textModel.Lines.Add("old line 2");
        var url = new Url("file:///test.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var mockMemoryFile = new Mock<IMemoryFile>();
        var content = Encoding.UTF8.GetBytes("New Line");
        mockMemoryFile.Setup(mf => mf.Content).Returns(content);

        mockFileSystem.Setup(fs => fs.ReadFileAsync(url))
            .ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.IsFalse(textModel.Lines.Contains("old line 1"));
        Assert.IsFalse(textModel.Lines.Contains("old line 2"));
    }

    [TestMethod]
    public async Task ReadFileAsync_WithUtf8File_DetectsEncoding()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///utf8.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var mockMemoryFile = new Mock<IMemoryFile>();
        var content = Encoding.UTF8.GetBytes("Test content");
        mockMemoryFile.Setup(mf => mf.Content).Returns(content);

        mockFileSystem.Setup(fs => fs.ReadFileAsync(url))
            .ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.IsNotNull(textModel.Encoding);
    }

    [TestMethod]
    public async Task ReadFileAsync_WithNullContentMemoryFile_HandlesGracefully()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///test.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns((byte[]?)null);

        mockFileSystem.Setup(fs => fs.ReadFileAsync(url))
            .ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    [TestMethod]
    public async Task ReadFileAsync_WithNullMemoryFile_HandlesGracefully()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///test.txt");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url))
            .ReturnsAsync((IMemoryFile?)null);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    // ── HTML extraction ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReadFileAsync_WithHtmlFile_ExtractsVisibleText()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///page.html");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var html = "<html><body><h1>Hello</h1><p>World</p></body></html>";
        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Encoding.UTF8.GetBytes(html));
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url)).ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        var joined = string.Join(" ", textModel.Lines);
        Assert.IsTrue(joined.Contains("Hello"), "Expected 'Hello' in extracted text");
        Assert.IsTrue(joined.Contains("World"), "Expected 'World' in extracted text");
    }

    [TestMethod]
    public async Task ReadFileAsync_WithHtmlFile_StripsScriptContent()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///page.html");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var html = "<html><body><p>Text</p><script>alert('secret');</script></body></html>";
        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Encoding.UTF8.GetBytes(html));
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url)).ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        var joined = string.Join(" ", textModel.Lines);
        Assert.IsTrue(joined.Contains("Text"), "Expected 'Text' in extracted output");
        Assert.IsFalse(joined.Contains("secret"), "Expected script content to be stripped");
    }

    [TestMethod]
    public async Task ReadFileAsync_WithHtmlFile_StripsStyleContent()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///page.html");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var html = "<html><head><style>body { color: red; }</style></head><body><p>Visible</p></body></html>";
        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Encoding.UTF8.GetBytes(html));
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url)).ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        var joined = string.Join(" ", textModel.Lines);
        Assert.IsTrue(joined.Contains("Visible"), "Expected 'Visible' in extracted output");
        Assert.IsFalse(joined.Contains("color"), "Expected style content to be stripped");
    }

    [TestMethod]
    public async Task ReadFileAsync_WithHtmExtension_ExtractsVisibleText()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///page.htm");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var html = "<html><body><p>HTM content</p></body></html>";
        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Encoding.UTF8.GetBytes(html));
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url)).ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        var joined = string.Join(" ", textModel.Lines);
        Assert.IsTrue(joined.Contains("HTM content"), "Expected 'HTM content' in extracted text");
    }

    [TestMethod]
    public async Task ReadFileAsync_WithHtmlFile_DecodesHtmlEntities()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///page.html");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var html = "<html><body><p>Hello &amp; World &lt;3&gt;</p></body></html>";
        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Encoding.UTF8.GetBytes(html));
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url)).ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        var joined = string.Join(" ", textModel.Lines);
        Assert.IsTrue(joined.Contains("Hello & World"), "Expected decoded entities in output");
    }

    [TestMethod]
    public async Task ReadFileAsync_WithHtmlFile_SkipsBlankLines()
    {
        var service = new FileToTextConverterService();
        var textModel = new TextModel();
        var url = new Url("file:///page.html");
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.ExistsFile(url)).Returns(true);

        var html = "<html><body><p>First</p><p>   </p><p>Last</p></body></html>";
        var mockMemoryFile = new Mock<IMemoryFile>();
        mockMemoryFile.Setup(mf => mf.Content).Returns(Encoding.UTF8.GetBytes(html));
        mockFileSystem.Setup(fs => fs.ReadFileAsync(url)).ReturnsAsync(mockMemoryFile.Object);

        await service.ReadFileAsync(url, mockFileSystem.Object, textModel);

        Assert.IsTrue(textModel.Lines.All(l => !string.IsNullOrWhiteSpace(l)),
            "Expected no blank lines in output");
    }
}
