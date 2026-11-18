using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class HexEditorControlTest
{
    private const int Width = 80;

    private static HexEditorControl Create(byte[] data, int height = 4)
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        var control = new HexEditorControl(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(Width, height),
        };
        control.OnInitializeLayout();
        control.Load(data);
        return control;
    }

    /// <summary>A key press as the keyboard makes it: the typed character, or the key itself for keys that type nothing.</summary>
    private static Task<bool> Type(HexEditorControl control, char c) =>
        control.HandleKeyEventAsync(new KeyEvent(c, ConsoleKey.NoName, false, false, false));

    private static Task<bool> Press(HexEditorControl control, ConsoleKey key, bool control1 = false) =>
        control.HandleKeyEventAsync(new KeyEvent('\0', key, false, false, control1));

    [Timeout(60_000)]
    [TestMethod]
    public async Task TwoHexDigits_OverwriteTheByteUnderTheCursorAndMoveOn()
    {
        var control = Create([0x00, 0x11]);

        await Type(control, 'a');
        await Type(control, 'F');

        CollectionAssert.AreEqual(new byte[] { 0xAF, 0x11 }, control.Data);
        Assert.AreEqual(1, control.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OneDigit_ChangesOnlyTheHighNibble()
    {
        var control = Create([0x5C]);

        await Type(control, 'a');

        Assert.AreEqual(0xAC, control.Data[0]);
        Assert.AreEqual(0, control.CursorIndex, "the byte is not finished until its low nibble is typed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingTheSameValue_IsNotAnEdit()
    {
        var control = Create([0xC3]);
        int edits = 0;
        control.Edited += () => edits++;

        await Type(control, 'c');
        await Type(control, '3');

        Assert.AreEqual(0, edits);
        CollectionAssert.AreEqual(new byte[] { 0xC3 }, control.Data);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EachDigitThatChangesAByte_RaisesEdited()
    {
        var control = Create([0x00, 0x00]);
        int edits = 0;
        control.Edited += () => edits++;

        await Type(control, '1');
        await Type(control, '2');

        Assert.AreEqual(2, edits, "the high nibble and then the low nibble each change the byte");
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x00 }, control.Data);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnly_ShowsTheBytesButTypesNothing()
    {
        var control = Create([0x41]);
        control.ReadOnlyCheck = () => true;

        await Type(control, '0');
        await Type(control, '0');

        CollectionAssert.AreEqual(new byte[] { 0x41 }, control.Data);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Arrows_MoveByByteAndByRow_AndStopAtTheEnds()
    {
        var control = Create(new byte[40]);

        await Press(control, ConsoleKey.RightArrow);
        Assert.AreEqual(1, control.CursorIndex);

        await Press(control, ConsoleKey.DownArrow);
        Assert.AreEqual(17, control.CursorIndex);

        await Press(control, ConsoleKey.LeftArrow);
        await Press(control, ConsoleKey.UpArrow);
        Assert.AreEqual(0, control.CursorIndex);

        await Press(control, ConsoleKey.LeftArrow);
        Assert.AreEqual(0, control.CursorIndex, "the cursor does not leave the file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlEnd_GoesToTheLastByte_AndEndToTheEndOfTheRow()
    {
        var control = Create(new byte[40]);

        await Press(control, ConsoleKey.End);
        Assert.AreEqual(15, control.CursorIndex);

        await Press(control, ConsoleKey.End, control1: true);
        Assert.AreEqual(39, control.CursorIndex);

        await Press(control, ConsoleKey.Home, control1: true);
        Assert.AreEqual(0, control.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MovingWithAHighNibbleTyped_LeavesItsHighNibbleAndStartsTheNextByteInItsHighNibble()
    {
        var control = Create([0x00, 0x00]);

        await Type(control, 'a');
        await Press(control, ConsoleKey.RightArrow);
        await Type(control, 'b');

        CollectionAssert.AreEqual(new byte[] { 0xA0, 0xB0 }, control.Data);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Draw_ShowsTheEditedByteInTheHexAndAsciiColumns()
    {
        var control = Create([0x41]);
        await Type(control, 'e');
        await Type(control, 'f');

        var screen = ScreenBuffer.Render(control, Width, 4);

        // The first byte is drawn at column 10 of its row, and its ASCII form at column 61 (after the gap in the middle of the row).
        Assert.AreEqual('E', screen.CharAt(10, 0));
        Assert.AreEqual('F', screen.CharAt(11, 0));
        Assert.AreEqual('.', screen.CharAt(61, 0), "0xEF is not printable");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListTheTypingAndMovingKeysForAShownDump()
    {
        var control = Create(new byte[16]);

        CollectionAssert.AreEqual(
            new[] { "0-9 A-F", "Arrows", "Home/End" },
            control.GetKeyHints().Select(hint => hint.Key).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_PutsTheCursorBackOnTheFirstByte()
    {
        var control = Create(new byte[40]);
        control.Load(new byte[4]);

        Assert.AreEqual(0, control.CursorIndex);
    }
}
