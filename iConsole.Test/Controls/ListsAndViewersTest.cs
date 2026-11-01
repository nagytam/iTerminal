using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ListsAndViewersTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    [Timeout(60_000)]
    [TestMethod]
    public void KeyValueList_AlignsValuesInAColumn()
    {
        MockBuffer buffer = new(new Size(30, 2));
        KeyValueList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 2) };
        list.SetItems([("Name", "iTerminal"), ("Version", "1.2.3")]);
        list.Draw(buffer);
        Assert.AreEqual("Name     iTerminal", Lines(buffer)[0].TrimEnd());
        Assert.AreEqual("Version  1.2.3", Lines(buffer)[1].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyValueList_CutsLongKeysAndValues()
    {
        KeyValueList list = new(Theme()) { MaxKeyWidth = 4, ViewSize = new Size(12, 1) };
        list.SetItems([("LongKey", "a very long value")]);
        MockBuffer buffer = new(new Size(12, 1));
        list.Position = new Position(0, 0);
        list.Draw(buffer);
        Assert.AreEqual(12, Lines(buffer)[0].Length);
        Assert.IsTrue(Lines(buffer)[0].StartsWith("Long  "));
    }

    private static CheckList CreateList()
    {
        CheckList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        list.SetItems(["a", "b", "c", "d", "e"], ["b"]);
        return list;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CheckList_Draw_ShowsCheckMarks()
    {
        MockBuffer buffer = new(new Size(20, 3));
        CreateList().Draw(buffer);
        Assert.AreEqual("[ ] a", Lines(buffer)[0].TrimEnd());
        Assert.AreEqual("[x] b", Lines(buffer)[1].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckList_Space_TogglesCursorRow()
    {
        CheckList list = CreateList();
        list.SetFocus();
        await list.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        CollectionAssert.AreEqual(new[] { "a", "b" }, list.CheckedItems.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckList_Star_InvertsAll()
    {
        CheckList list = CreateList();
        list.SetFocus();
        await list.HandleKeyEventAsync(Key(ConsoleKey.Multiply, '*'));
        CollectionAssert.AreEqual(new[] { "a", "c", "d", "e" }, list.CheckedItems.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckList_Click_MovesCursorAndToggles()
    {
        CheckList list = CreateList();
        await list.HandleMouseEventAsync(Click(2, 2));
        Assert.AreEqual(2, list.CursorIndex);
        Assert.IsTrue(list.CheckedItems.Contains("c"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckList_CursorPastView_Scrolls()
    {
        CheckList list = CreateList();
        list.SetFocus();
        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        MockBuffer buffer = new(new Size(20, 3));
        list.Draw(buffer);
        Assert.AreEqual("[ ] e", Lines(buffer)[2].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckList_ReadOnly_DoesNotToggle()
    {
        CheckList list = CreateList();
        list.IsReadOnly = true;
        await list.HandleMouseEventAsync(Click(2, 0));
        Assert.AreEqual(1, list.CheckedItems.Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexViewer_Draw_BlanksRowsPastTheData()
    {
        HexViewer viewer = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 3) };
        viewer.Data = new byte[48];
        MockBuffer buffer = new(new Size(80, 3));
        viewer.Draw(buffer);
        viewer.Data = new byte[16];
        viewer.Draw(buffer);
        Assert.AreEqual(string.Empty, Lines(buffer)[2].Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HexViewer_Click_MovesCursorRow()
    {
        HexViewer viewer = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 3) };
        viewer.OnInitializeLayout();
        viewer.Data = new byte[64];
        await viewer.HandleMouseEventAsync(Click(5, 2));
        Assert.AreEqual(32, viewer.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextViewer_Wheel_MovesCursorThreeRows()
    {
        TextViewerControl viewer = new(false, Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 6) };
        viewer.TextModel.Lines = [.. Enumerable.Range(0, 20).Select(i => "line " + i)];
        viewer.ScrollableModel.ScrollableSize = new Size(10, 20);
        viewer.ScrollableModel.ViewSize = new Size(39, 5);
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), WheelDelta = -1 });
        Assert.AreEqual(3, (int)viewer.ScrollableModel.CursorPosition.Y);
    }
}
