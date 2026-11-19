using System.Text;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Dialogs.Test;

[TestClass]
public class TextFileEditorHexViewTest
{
    private static readonly Url FileUrl = new("file://C:/x/note.txt");

    /// <summary>An editor opened on a file whose bytes are <paramref name="fileBytes"/>, and whose text reads as <paramref name="lines"/>.</summary>
    private static async Task<(TextFileEditorDialog Dialog, Mock<IContentModel> Model, List<byte[]> Writes)> OpenAsync(
        byte[] fileBytes, string[]? lines, bool asHex = false)
    {
        var model = new Mock<IContentModel>();
        var memory = new Mock<IMemoryFile>();
        memory.SetupGet(m => m.Content).Returns(fileBytes);
        model.Setup(m => m.ReadFileAsync(It.IsAny<Url>())).ReturnsAsync(memory.Object);

        var writes = new List<byte[]>();
        model.Setup(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()))
            .Callback<Url, MemoryStream>((_, stream) => writes.Add(stream.ToArray()))
            .Returns(Task.CompletedTask);

        var contentModel = new Mock<IContentModelService>();
        contentModel.Setup(c => c.GetFocusedModel()).Returns(model.Object);
        contentModel.Setup(c => c.GetLastContentModel()).Returns(model.Object);

        var converter = new Mock<IFileToTextConverterService>();
        converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, text) =>
            {
                text.LineEnding = "\n";
                text.Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                text.Lines.Clear();
                foreach (string line in lines ?? [])
                {
                    text.Lines.Add(line);
                }
            })
            .Returns(Task.CompletedTask);

        var dialog = new TextFileEditorDialog(
            contentModel.Object, converter.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService());
        dialog.Url = FileUrl;
        dialog.OpenAsHex = asHex;
        await dialog.OnInitializeAsync();
        return (dialog, model, writes);
    }

    private static Task<bool> PressF4(TextFileEditorDialog dialog) =>
        dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F4, false, false, false));

    [Timeout(60_000)]
    [TestMethod]
    public async Task F4ShowsTheBytesOfTheText_AndF4BackKeepsTheText()
    {
        var (dialog, _, _) = await OpenAsync(Encoding.UTF8.GetBytes("hello\n"), ["hello"]);

        await PressF4(dialog);

        Assert.IsTrue(dialog.IsHexMode);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("hello\n"), dialog.HexEditorControl.Data);

        await PressF4(dialog);

        Assert.IsFalse(dialog.IsHexMode);
        CollectionAssert.AreEqual(new[] { "hello" }, dialog.TextEditorControl.TextModel.Lines.ToArray());
        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AByteEditedInTheHexView_IsSavedAsThoseBytes()
    {
        var (dialog, _, writes) = await OpenAsync(Encoding.UTF8.GetBytes("hello\n"), ["hello"]);
        await PressF4(dialog);

        // The 'h' becomes 'H': the high nibble 4, then the low nibble 8.
        await dialog.HandleKeyEventAsync(new KeyEvent('4', ConsoleKey.D4, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('8', ConsoleKey.D8, false, false, false));
        Assert.IsTrue(dialog.TextEditorControl.IsDirty);

        await dialog.SaveAsync();

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("Hello\n"), writes.Single());
        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AHexEditThatDoesNotReadBackAsText_KeepsTheHexView()
    {
        // 0xFF is not valid UTF-8, so the edited bytes cannot be shown as text without changing them.
        var (dialog, _, writes) = await OpenAsync([0x41, 0xFF, 0x0A], null);
        await PressF4(dialog);
        await dialog.HandleKeyEventAsync(new KeyEvent('0', ConsoleKey.D0, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('0', ConsoleKey.D0, false, false, false));

        await PressF4(dialog);

        Assert.IsTrue(dialog.IsHexMode, "the editor does not leave the hex view for text that would not save as these bytes");
        Assert.IsTrue(dialog.Title.Contains("Not text"), dialog.Title);
        Assert.AreEqual(0, writes.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABinaryFile_OpensAsBytes_AndLeavesOnlyWhenItReadsBackAsText()
    {
        // FF and FE are not valid UTF-8, so these bytes cannot be read as text.
        var (dialog, _, _) = await OpenAsync([0xFF, 0xFE], null, asHex: true);
        await dialog.ToggleHexModeAsync();

        Assert.IsTrue(dialog.IsHexMode);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFE }, dialog.HexEditorControl.Data);

        await dialog.ToggleHexModeAsync();

        Assert.IsTrue(dialog.IsHexMode, "the bytes are not text, so they stay in the hex view");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABinaryFile_WhoseBytesAreText_LeavesToTheTextView()
    {
        var (dialog, _, _) = await OpenAsync(Encoding.ASCII.GetBytes("abc\n"), null, asHex: true);
        await dialog.ToggleHexModeAsync();

        await dialog.ToggleHexModeAsync();

        Assert.IsFalse(dialog.IsHexMode);
        CollectionAssert.AreEqual(new[] { "abc", "" }, dialog.TextEditorControl.TextModel.Lines.ToArray(), "a file ends with a line break, so its last line is empty");
        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TryDecodeExactly_ReadsTextThatEncodesBackToTheSameBytes()
    {
        bool ok = TextFileEditorDialog.TryDecodeExactly(Encoding.UTF8.GetBytes("a\nb"), new UTF8Encoding(false), "\n", out string[] lines, out string lineEnding);

        Assert.IsTrue(ok);
        CollectionAssert.AreEqual(new[] { "a", "b" }, lines);
        Assert.AreEqual("\n", lineEnding);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TryDecodeExactly_KeepsAByteOrderMarkTheFileHad()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'x', (byte)'\n'];

        bool ok = TextFileEditorDialog.TryDecodeExactly(bytes, Encoding.UTF8, "\n", out string[] lines, out _);

        Assert.IsTrue(ok);
        CollectionAssert.AreEqual(new[] { "x", "" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TryDecodeExactly_RefusesMixedLineBreaks()
    {
        bool ok = TextFileEditorDialog.TryDecodeExactly(Encoding.UTF8.GetBytes("a\r\nb\n"), new UTF8Encoding(false), "\n", out _, out _);

        Assert.IsFalse(ok, "one line break would be written for both, which changes the file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TryDecodeExactly_RefusesBytesThatAreNotValidInTheCodePage()
    {
        bool ok = TextFileEditorDialog.TryDecodeExactly([0xFF], new UTF8Encoding(false), "\n", out _, out _);

        Assert.IsFalse(ok);
    }
}
