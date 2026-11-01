using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class TableTest
{
    private record Person(string Name, int Age, string City);

    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    private static Table<Person> CreateTable(
        IEnumerable<Person> rows,
        int width = 40,
        int height = 5)
    {
        var collection = new ConcurrentObservableCollection<Person>(rows);
        var table = new Table<Person>(collection, ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(width, height)
        };
        table.Columns.Add(new ColumnDefinition<Person>("Name", 10, p => p.Name));
        table.Columns.Add(new ColumnDefinition<Person>("Age",   4, p => p.Age.ToString()));
        table.Columns.Add(new ColumnDefinition<Person>("City", 12, p => p.City));
        table.OnInitializeLayout();
        return table;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListThePagingAndJumpKeys()
    {
        var keys = CreateTable([Alice, Bob]).GetKeyHints().Select(h => h.Key).ToList();

        CollectionAssert.Contains(keys, "PageUp/PageDown");
        CollectionAssert.Contains(keys, "Home/End");
    }

    private static readonly Person Alice = new("Alice", 30, "New York");
    private static readonly Person Bob   = new("Bob",   25, "London");
    private static readonly Person Carol = new("Carol", 35, "Paris");

    // ── Header ─────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_RendersHeaderOnFirstRow()
    {
        var buffer = new MockBuffer(new Size(40, 5));
        CreateTable([Alice]).Draw(buffer);

        var firstRow = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.IsTrue(firstRow.Contains("Name"));
        Assert.IsTrue(firstRow.Contains("Age"));
        Assert.IsTrue(firstRow.Contains("City"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_HeaderColumnsAppearInOrder()
    {
        var buffer = new MockBuffer(new Size(40, 5));
        CreateTable([Alice]).Draw(buffer);

        var firstRow = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.IsTrue(firstRow.IndexOf("Name", StringComparison.Ordinal)
                    < firstRow.IndexOf("Age",  StringComparison.Ordinal));
        Assert.IsTrue(firstRow.IndexOf("Age",  StringComparison.Ordinal)
                    < firstRow.IndexOf("City", StringComparison.Ordinal));
    }

    // ── Data rows ──────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_SingleRow_RendersDataBelowHeader()
    {
        var buffer = new MockBuffer(new Size(40, 5));
        CreateTable([Alice]).Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[1].Contains("Alice"));
        Assert.IsTrue(lines[1].Contains("30"));
        Assert.IsTrue(lines[1].Contains("New York"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_MultipleRows_EachOnItsOwnLine()
    {
        var buffer = new MockBuffer(new Size(40, 5));
        CreateTable([Alice, Bob, Carol]).Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[1].Contains("Alice"));
        Assert.IsTrue(lines[2].Contains("Bob"));
        Assert.IsTrue(lines[3].Contains("Carol"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_EmptyCollection_RendersOnlyHeader()
    {
        var buffer = new MockBuffer(new Size(40, 5));
        CreateTable([]).Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].Contains("Name"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(lines[1]));
    }

    // ── Column truncation ──────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_LongValue_TruncatedToColumnWidth()
    {
        var buffer = new MockBuffer(new Size(40, 5));
        var person = new Person("VeryLongNameIndeed", 30, "City");
        CreateTable([person]).Draw(buffer);

        // Name column is 10 chars wide; "VeryLongNa" is the truncated form
        var dataRow = buffer.ContentAsString.Split(Environment.NewLine)[1];
        Assert.IsTrue(dataRow.Contains("VeryLongNa"));
        Assert.IsFalse(dataRow.Contains("VeryLongNameIndeed"));
    }

    // ── Cursor / selection ─────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void CursorRow_DefaultsToZero()
    {
        var table = CreateTable([Alice, Bob]);
        Assert.AreEqual(0, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SelectedRow_DefaultsToFirstRow()
    {
        var table = CreateTable([Alice, Bob]);
        Assert.AreEqual(Alice, table.SelectedRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SelectedRow_EmptyCollection_ReturnsDefault()
    {
        var table = CreateTable([]);
        Assert.IsNull(table.SelectedRow);
    }

    // ── Keyboard navigation ────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task DownArrow_MovesCursorToNextRow()
    {
        var table = CreateTable([Alice, Bob, Carol]);

        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.AreEqual(1, table.CursorRow);
        Assert.AreEqual(Bob, table.SelectedRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpArrow_AtFirstRow_StaysAtZero()
    {
        var table = CreateTable([Alice, Bob]);

        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.AreEqual(0, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeKey_JumpsToFirstRow()
    {
        var table = CreateTable([Alice, Bob, Carol]);
        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, false));

        Assert.AreEqual(0, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EndKey_JumpsToLastRow()
    {
        var table = CreateTable([Alice, Bob, Carol]);

        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false));

        Assert.AreEqual(2, table.CursorRow);
    }

    // ── RowActivated event ─────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterKey_FiresRowActivatedWithSelectedRow()
    {
        var table = CreateTable([Alice, Bob]);
        Person? activated = null;
        table.RowActivated += (_, row) => activated = row;
        table.SetFocus();

        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual(Alice, activated);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterKey_AfterNavigation_FiresCorrectRow()
    {
        var table = CreateTable([Alice, Bob, Carol]);
        Person? activated = null;
        table.RowActivated += (_, row) => activated = row;
        table.SetFocus();

        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Enter,     false, false, false));

        Assert.AreEqual(Bob, activated);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterKey_ReturnsHandledTrue()
    {
        var table = CreateTable([Alice]);
        table.SetFocus();

        var handled = await table.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Enter, false, false, false));

        Assert.IsTrue(handled);
    }

    // ── Live collection updates ────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void AddRow_AfterConstruction_AppearsInNextDraw()
    {
        var collection = new ConcurrentObservableCollection<Person>();
        var table = new Table<Person>(collection, ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(40, 5)
        };
        table.Columns.Add(new ColumnDefinition<Person>("Name", 10, p => p.Name));
        table.OnInitializeLayout();

        collection.Add(Alice);

        var buffer = new MockBuffer(new Size(40, 5));
        table.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Alice"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ClearRows_AfterConstruction_DrawsNoDataRows()
    {
        var collection = new ConcurrentObservableCollection<Person>([Alice, Bob]);
        var table = new Table<Person>(collection, ThemeMock().Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(40, 5)
        };
        table.Columns.Add(new ColumnDefinition<Person>("Name", 10, p => p.Name));
        table.OnInitializeLayout();

        collection.Clear();

        var buffer = new MockBuffer(new Size(40, 5));
        table.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(string.IsNullOrWhiteSpace(lines[1]));
    }

    // ── Column definition ──────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ColumnDefinition_ExposesHeaderWidthAndSelector()
    {
        var col = new ColumnDefinition<Person>("Name", 10, p => p.Name);

        Assert.AreEqual("Name", col.Header);
        Assert.AreEqual(10, col.Width);
        Assert.AreEqual("Alice", col.ValueSelector(Alice));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterKey_WhenNotFocused_DoesNotActivateARow()
    {
        var table = CreateTable([Alice, Bob]);
        bool activated = false;
        table.RowActivated += (_, _) => activated = true;

        bool handled = await table.HandleKeyEventAsync(EnterKey);

        Assert.IsFalse(activated, "a table that is not focused must leave Enter to its host");
        Assert.IsFalse(handled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterKey_WhenDisabled_DoesNotActivateARow()
    {
        var table = CreateTable([Alice]);
        table.SetFocus();
        table.IsEnabled = false;
        bool activated = false;
        table.RowActivated += (_, _) => activated = true;

        await table.HandleKeyEventAsync(EnterKey);

        Assert.IsFalse(activated);
    }

    private static readonly KeyEvent EnterKey = new('\0', ConsoleKey.Enter, false, false, false);
}
