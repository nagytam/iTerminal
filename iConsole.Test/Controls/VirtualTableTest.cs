using iConsole.Controls;
using iConsole.Data;
using iConsole.Model;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class VirtualTableTest
{
    private readonly TestThemeService _theme = new();

    private sealed record Person(int Id, string Name);

    private sealed class PeopleSource(int count) : IVirtualDataSource<Person>
    {
        public int Count { get; } = count;
        public HashSet<int> Read { get; } = [];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public bool TryGet(int index, out Person? item)
        {
            Read.Add(index);
            item = index >= 0 && index < Count ? new Person(index, "Person " + index) : null;
            return item is not null;
        }

        public void Prefetch(int start, int count)
        {
        }
    }

    private VirtualTable<Person> Create(PeopleSource source, int width = 30, int height = 5)
    {
        VirtualTable<Person> table = new(source, _theme) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        table.Columns.Add(new ColumnDefinition<Person>("Id", 8, p => p.Id.ToString()));
        table.Columns.Add(new ColumnDefinition<Person>("Name", 12, p => p.Name));
        table.SetFocus();
        return table;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrawsTheHeaderAndOnlyTheVisibleRows()
    {
        PeopleSource source = new(5_000_000);
        VirtualTable<Person> table = Create(source);
        ScreenBuffer buffer = new(30, 5);

        table.Draw(buffer);

        Assert.IsTrue(buffer.Line(0).StartsWith("Id       Name", StringComparison.Ordinal));
        Assert.IsTrue(buffer.Line(1).StartsWith("0        Person 0", StringComparison.Ordinal));
        Assert.IsTrue(buffer.Line(4).StartsWith("3        Person 3", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, source.Read.ToArray(), "four body rows under the header");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScrollsTheBodyUnderAFixedHeader()
    {
        VirtualTable<Person> table = Create(new PeopleSource(1000));

        await table.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.End, false, false, false));
        ScreenBuffer buffer = new(30, 5);
        table.Draw(buffer);

        Assert.AreEqual(999, table.CursorIndex);
        Assert.IsTrue(buffer.Line(0).StartsWith("Id", StringComparison.Ordinal));
        Assert.IsTrue(buffer.Line(4).StartsWith("999", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSortedColumnShowsAnArrow()
    {
        VirtualTable<Person> table = Create(new PeopleSource(10));
        table.SortColumn = 1;

        Assert.IsTrue(table.HeaderText(1).EndsWith('▲'));

        table.SortDescending = true;
        Assert.IsTrue(table.HeaderText(1).EndsWith('▼'));
        Assert.AreEqual("Id", table.HeaderText(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnAHeaderCellRaisesSortRequested()
    {
        VirtualTable<Person> table = Create(new PeopleSource(10));
        List<int> requested = [];
        table.SortRequested += (_, column) => requested.Add(column);

        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 0), LeftClicked = true });

        CollectionAssert.AreEqual(new[] { 1 }, requested);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickBetweenColumnsDoesNotSort()
    {
        VirtualTable<Person> table = Create(new PeopleSource(10));
        List<int> requested = [];
        table.SortRequested += (_, column) => requested.Add(column);

        // Column 0 is 8 wide, then one separator cell at x = 8.
        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(8, 0), LeftClicked = true });

        Assert.AreEqual(0, requested.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnABodyRowMovesTheCursorBelowTheHeader()
    {
        VirtualTable<Person> table = Create(new PeopleSource(100));

        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 3), LeftClicked = true });

        Assert.AreEqual(2, table.CursorIndex, "row 3 on screen is the second body row after the header");
    }
}
