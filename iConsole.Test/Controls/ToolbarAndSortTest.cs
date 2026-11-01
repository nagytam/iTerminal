using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ToolbarAndSortTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    private static Toolbar Create()
    {
        Toolbar toolbar = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        toolbar.Items = [new ToolbarItem("Open", "F3"), new ToolbarItem("Save", IsEnabled: false), new ToolbarItem("Close")];
        return toolbar;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Toolbar_Draw_ShowsCaptionsAndHints()
    {
        MockBuffer buffer = new(new Size(40, 1));
        Create().Draw(buffer);
        Assert.AreEqual(" Open F3   Save   Close", Lines(buffer)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Toolbar_Arrows_SkipDisabledItems()
    {
        Toolbar toolbar = Create();
        toolbar.SetFocus();
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, toolbar.ActiveIndex);
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, toolbar.ActiveIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Toolbar_Enter_InvokesActiveItem()
    {
        Toolbar toolbar = Create();
        toolbar.SetFocus();
        int invoked = -1;
        toolbar.ItemInvoked += (_, i) => invoked = i;
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(0, invoked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Toolbar_Click_InvokesEnabledItemOnly()
    {
        Toolbar toolbar = Create();
        List<int> invoked = [];
        toolbar.ItemInvoked += (_, i) => invoked.Add(i);
        await toolbar.HandleMouseEventAsync(Click(13, 0));
        Assert.AreEqual(0, invoked.Count);
        await toolbar.HandleMouseEventAsync(Click(19, 0));
        CollectionAssert.AreEqual(new[] { 2 }, invoked);
    }

    private static Table<string> CreateTable()
    {
        Table<string> table = new(new ConcurrentObservableCollection<string>(["a", "b"]), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 4),
        };
        table.Columns.Add(new ColumnDefinition<string>("Name", 10, s => s));
        table.Columns.Add(new ColumnDefinition<string>("Size", 8, s => s));
        table.OnInitializeLayout();
        return table;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Table_ColumnAt_MapsHeaderCells()
    {
        Table<string> table = CreateTable();
        Assert.AreEqual(0, table.ColumnAt(3));
        Assert.AreEqual(-1, table.ColumnAt(10));
        Assert.AreEqual(1, table.ColumnAt(11));
        Assert.AreEqual(-1, table.ColumnAt(25));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Table_HeaderClick_RaisesSortRequested()
    {
        Table<string> table = CreateTable();
        int requested = -1;
        table.SortRequested += (_, column) => requested = column;
        await table.HandleMouseEventAsync(Click(12, 0));
        Assert.AreEqual(1, requested);
        Assert.AreEqual(0, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Table_SortedColumn_ShowsArrowInHeader()
    {
        Table<string> table = CreateTable();
        table.SortColumn = 0;
        MockBuffer buffer = new(new Size(30, 4));
        table.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[0].StartsWith("Name     ▲"));
        table.SortDescending = true;
        table.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[0].StartsWith("Name     ▼"));
    }
}
