using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class HexViewerTest
{
    // Width for a standard 16-byte row: 8(offset) + 2 + 47(hex) + 2 + 18(ascii) = 79 chars.
    // We use 80 to give one character of padding.
    private const int StandardWidth = 80;

    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListTheScrollKeysForAShownDump()
    {
        var hv = Create(new byte[64]);
        var keys = hv.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.AreEqual(new[] { "Up/Down", "PageUp/PageDown", "Home/End" }, keys);

        hv.Data = [];
        Assert.AreEqual(0, hv.GetKeyHints().Count());
    }

    private static HexViewer Create(byte[]? data = null, int height = 4)
    {
        var hv = new HexViewer(ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(StandardWidth, height)
        };
        hv.OnInitializeLayout();
        if (data is not null) hv.Data = data;
        return hv;
    }

    // ── Default state ───────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void DefaultData_IsEmpty()
    {
        Assert.AreEqual(0, Create().Data.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DefaultSelectedOffset_IsZero()
    {
        Assert.AreEqual(0, Create().SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanFocus_IsTrue()
    {
        Assert.IsTrue(Create().CanFocus);
    }

    // ── SelectedOffset ──────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task DownArrow_AdvancesSelectedOffsetByBytesPerRow()
    {
        var hv = Create(data: new byte[64]);

        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.AreEqual(hv.BytesPerRow, hv.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpArrow_AtFirstRow_StaysAtZero()
    {
        var hv = Create(data: new byte[64]);

        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.AreEqual(0, hv.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeKey_JumpsToFirstRow()
    {
        var hv = Create(data: new byte[128]);
        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, false));

        Assert.AreEqual(0, hv.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EndKey_JumpsToLastRow()
    {
        var data = new byte[48]; // 3 rows of 16
        var hv = Create(data: data);

        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false));

        Assert.AreEqual(32, hv.SelectedOffset); // row 2 = offset 32
    }

    // ── Data assignment ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void AssigningData_ResetsCursorToZero()
    {
        var hv = Create(data: new byte[64]);
        hv.ScrollableModel.CursorPosition.Y = 2;

        hv.Data = new byte[32];

        Assert.AreEqual(0, hv.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AssigningNull_TreatedAsEmpty()
    {
        var hv = Create(data: new byte[32]);
        hv.Data = null!;
        Assert.AreEqual(0, hv.Data.Length);
    }

    // ── Rendering — offset column ───────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_FirstRow_ShowsZeroOffset()
    {
        var hv = Create(data: new byte[16]);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("00000000"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_SecondRow_ShowsCorrectOffset()
    {
        var hv = Create(data: new byte[32]);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        // Second row starts at offset 16 = 0x10
        Assert.IsTrue(buffer.ContentAsString.Contains("00000010"));
    }

    // ── Rendering — hex column ──────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_KnownBytes_ShowsCorrectHexValues()
    {
        var hv = Create(data: [0xDE, 0xAD, 0xBE, 0xEF]);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("DE"));
        Assert.IsTrue(content.Contains("AD"));
        Assert.IsTrue(content.Contains("BE"));
        Assert.IsTrue(content.Contains("EF"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PartialRow_PadsHexColumnsWithSpaces()
    {
        // Only 4 bytes — the remaining 12 hex positions should be blank
        var hv = Create(data: [0x01, 0x02, 0x03, 0x04]);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        // Row should still be padded to full width
        var firstLine = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual(StandardWidth, firstLine.Length);
    }

    // ── Rendering — ASCII column ────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PrintableBytes_ShownAsCharacters()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("Hello");
        var hv = Create(data: data);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Hello"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_NonPrintableBytes_ShownAsDot()
    {
        var hv = Create(data: [0x00, 0x01, 0x1F, 0x7F]);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        // ASCII column should contain dots for each non-printable byte
        var asciiSection = buffer.ContentAsString;
        Assert.IsTrue(asciiSection.Contains("...."));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AsciiColumnDelimitedByPipes()
    {
        var hv = Create(data: new byte[16]);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("|"));
    }

    // ── Rendering — scrolling ───────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task Draw_AfterScrollDown_ShowsNextRow()
    {
        // Row 0: offset 0x00, row 1: offset 0x10
        var data = new byte[32];
        data[16] = 0xAB;
        var hv = Create(data: data);

        await hv.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        var buffer = new MockBuffer(new Size(StandardWidth, 4));
        hv.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("AB"));
    }

    // ── Empty data ──────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_EmptyData_WritesNothing()
    {
        var hv = Create(data: []);
        var buffer = new MockBuffer(new Size(StandardWidth, 4));

        hv.Draw(buffer);

        Assert.IsTrue(string.IsNullOrWhiteSpace(buffer.ContentAsString));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Disabled_IgnoresTheWheelAndClicks()
    {
        var hv = Create(new byte[16 * 10]);
        hv.IsEnabled = false;
        long before = hv.ScrollableModel.CursorPosition.Y;

        Assert.IsFalse(await hv.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 1), WheelDelta = -1 }));
        Assert.IsFalse(await hv.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 2), LeftClicked = true }));

        Assert.AreEqual(before, hv.ScrollableModel.CursorPosition.Y);
        Assert.IsFalse(hv.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enabled_TheWheelStillMovesTheCursorThreeRows()
    {
        var hv = Create(new byte[16 * 10]);

        Assert.IsTrue(await hv.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 1), WheelDelta = -1 }));

        Assert.AreEqual(3L, hv.ScrollableModel.CursorPosition.Y);
    }
}
