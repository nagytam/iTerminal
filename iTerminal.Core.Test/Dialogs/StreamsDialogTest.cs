using System.Text;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Streams;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class StreamsDialogTest
{
    private const string Target = @"C:\data\report.docx";

    private Mock<IStreamAttributeService> _service = null!;
    private StreamsDialog _dialog = null!;
    private List<StreamItem> _items = null!;
    private int _lists;

    [TestInitialize]
    public void Setup()
    {
        _items =
        [
            new StreamItem("Zone.Identifier", 26),
            new StreamItem("blob", 5),
            new StreamItem("empty", 0),
        ];
        _service = new Mock<IStreamAttributeService>();
        _service.SetupGet(s => s.Kind).Returns("Streams");
        _service.Setup(s => s.ListAsync(Target, It.IsAny<CancellationToken>()))
            .Returns(() => { Interlocked.Increment(ref _lists); return Task.FromResult(new StreamListing([.. _items])); });
        _service.Setup(s => s.ReadAsync(Target, "Zone.Identifier", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamData(Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3"), 26));
        _service.Setup(s => s.ReadAsync(Target, "blob", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamData([0x00, 0x01, 0xfe, 0x41, 0x42], 5));
        _service.Setup(s => s.ReadAsync(Target, "empty", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamData([], 0));
        _service.Setup(s => s.DeleteAsync(Target, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string name, CancellationToken _) =>
            {
                _items.RemoveAll(i => i.Name == name);
                return Task.FromResult(new StreamResult(true, "Deleted " + name + "."));
            });
        _dialog = new StreamsDialog(_service.Object, UiTestHelper.TestTheme());
    }

    private async Task OpenAsync()
    {
        _dialog.Load(Target);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.CountForTest == 3, "the list to be read");
        await _dialog.PendingOperation;
    }

    private iConsole.Test.Core.ScreenBuffer Screen() => UiTestHelper.Render(_dialog);

    private async Task PressAsync(ConsoleKey key, char keyChar = '\0')
    {
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(key, keyChar));
        await _dialog.PendingOperation;
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Opening_ListsEachStreamWithItsSize_AndPreviewsTheFirstAsText()
    {
        await OpenAsync();

        var screen = Screen();
        UiTestHelper.AssertScreenContains(screen, "report.docx");
        UiTestHelper.AssertScreenContains(screen, "Zone.Identifier");
        UiTestHelper.AssertScreenContains(screen, "blob");
        UiTestHelper.AssertScreenContains(screen, "(text)");
        UiTestHelper.AssertScreenContains(screen, "ZoneId=3");
        Screen();
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task MovingTheCursor_ShowsBinaryAsHex_AndHSwitchesBack()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.DownArrow);
        await UiTestHelper.WaitUntilAsync(() => Screen().ToString()!.Contains("(hex)"), "the hex preview");
        UiTestHelper.AssertScreenContains(Screen(), "00 01 fe 41 42");

        await PressAsync(ConsoleKey.H, 'h');
        UiTestHelper.AssertScreenContains(Screen(), "(text)");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AnEmptyStream_SaysItIsEmpty()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.End);
        await UiTestHelper.WaitUntilAsync(() => Screen().ToString()!.Contains("(empty)"), "the empty preview");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Delete_AsksInTheFooter_AndYDeletesThenReadsTheListAgain()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.Delete);
        UiTestHelper.AssertScreenContains(Screen(), "Delete Zone.Identifier? y deletes it.");
        UiTestHelper.AssertScreenContains(Screen(), "any other key keeps it");
        _service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await PressAsync(ConsoleKey.Y, 'y');
        await UiTestHelper.WaitUntilAsync(() => _dialog.CountForTest == 2, "the list without the stream");

        _service.Verify(s => s.DeleteAsync(Target, "Zone.Identifier", It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(2, _lists, "the list is read again, not assumed");
        UiTestHelper.AssertScreenContains(Screen(), "Deleted Zone.Identifier.");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Delete_AnyOtherKeyKeepsIt_EscapeIncluded()
    {
        await OpenAsync();

        await PressAsync(ConsoleKey.Delete);
        await PressAsync(ConsoleKey.Escape);
        await PressAsync(ConsoleKey.Delete);
        await PressAsync(ConsoleKey.N, 'n');

        _service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(3, _dialog.CountForTest);
        UiTestHelper.AssertScreenDoesNotContain(Screen(), "y deletes it");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AFailedDelete_IsShownAsAnError_AndTheStreamStays()
    {
        _service.Setup(s => s.DeleteAsync(Target, "Zone.Identifier", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamResult(false, "Access is denied."));
        await OpenAsync();

        await PressAsync(ConsoleKey.Delete);
        await PressAsync(ConsoleKey.Y, 'y');
        await UiTestHelper.WaitUntilAsync(() => _dialog.StatusForTest == "Access is denied.", "the failure");

        Assert.AreEqual(3, _dialog.CountForTest);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task NoStreams_SaysSo_AndDeleteDoesNothing()
    {
        _items.Clear();
        _dialog.Load(Target);
        await UiTestHelper.ShowAsync(_dialog);
        await _dialog.PendingOperation;
        await UiTestHelper.WaitUntilAsync(() => Screen().ToString()!.Contains("has no streams"), "the empty list");

        await PressAsync(ConsoleKey.Delete);

        Assert.AreEqual(string.Empty, _dialog.StatusForTest);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AListFailure_IsShownInTheList()
    {
        _service.Setup(s => s.ListAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(new StreamListing([], "Access is denied."));
        _dialog.Load(Target);
        await UiTestHelper.ShowAsync(_dialog);
        await _dialog.PendingOperation;

        await UiTestHelper.WaitUntilAsync(() => Screen().ToString()!.Contains("Access is denied."), "the failure");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AStalePreview_IsDropped_WhenTheCursorMovedOn()
    {
        TaskCompletionSource<StreamData> slow = new();
        _service.Setup(s => s.ReadAsync(Target, "Zone.Identifier", It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(slow.Task);
        _dialog.Load(Target);
        await UiTestHelper.ShowAsync(_dialog);
        await UiTestHelper.WaitUntilAsync(() => _dialog.CountForTest == 3, "the list");

        await PressAsync(ConsoleKey.DownArrow);
        slow.SetResult(new StreamData(Encoding.UTF8.GetBytes("STALE-TEXT"), 10));
        await _dialog.PendingOperation;
        await UiTestHelper.WaitUntilAsync(() => Screen().ToString()!.Contains("(hex)"), "the preview of the second entry");

        UiTestHelper.AssertScreenDoesNotContain(Screen(), "STALE-TEXT");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ATruncatedPreview_SaysHowMuchIsShown()
    {
        _service.Setup(s => s.ReadAsync(Target, "Zone.Identifier", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamData(Encoding.UTF8.GetBytes("abc"), 5000));
        await OpenAsync();

        await UiTestHelper.WaitUntilAsync(() => Screen().ToString()!.Contains("first 3 B of"), "the truncation note");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task KeyHints_FollowTheQuestion()
    {
        await OpenAsync();
        CollectionAssert.IsSubsetOf(new[] { "Up/Down", "H", "Delete", "F5" }, _dialog.GetKeyHints().Select(h => h.Key).ToArray());

        await PressAsync(ConsoleKey.Delete);

        CollectionAssert.AreEqual(new[] { "Y", "Any other key" }, _dialog.GetKeyHints().Select(h => h.Key).ToArray());
    }

    [TestMethod]
    [Timeout(15_000)]
    public void HexLines_ShowsOffsetBytesAndPrintableCharacters()
    {
        IReadOnlyList<string> lines = StreamsDialog.HexLines(Encoding.ASCII.GetBytes("Hello, hex dump of 20 bytes"), 5);

        Assert.AreEqual(2, lines.Count);
        StringAssert.StartsWith(lines[0], "000000  48 65 6c 6c 6f 2c 20 68");
        StringAssert.EndsWith(lines[0], "Hello, hex dump ");
        StringAssert.StartsWith(lines[1], "000010  6f 66");
        Assert.AreEqual(1, StreamsDialog.HexLines(new byte[100], 1).Count);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void LooksLikeBinary_TellsTextFromBytes()
    {
        Assert.IsFalse(StreamsDialog.LooksLikeBinary(new StreamData(Encoding.UTF8.GetBytes("plain\r\n\ttext \u00e9"), 11)));
        Assert.IsFalse(StreamsDialog.LooksLikeBinary(new StreamData([], 0)));
        Assert.IsTrue(StreamsDialog.LooksLikeBinary(new StreamData([0x41, 0x00], 2)), "a NUL");
        Assert.IsTrue(StreamsDialog.LooksLikeBinary(new StreamData([0xff, 0xfe, 0x41], 3)), "invalid UTF-8");
        byte[] cut = [.. Encoding.UTF8.GetBytes("ab"), 0xc3];
        Assert.IsFalse(StreamsDialog.LooksLikeBinary(new StreamData(cut, 99)), "a read cut inside a character is still text");
        Assert.IsTrue(StreamsDialog.LooksLikeBinary(new StreamData(cut, 3)), "the same bytes at the true end are not");
    }
}
