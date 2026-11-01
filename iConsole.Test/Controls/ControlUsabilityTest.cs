using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ControlUsabilityTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Table<string> CreateTable(params string[] rows)
    {
        Table<string> table = new(new ConcurrentObservableCollection<string>(rows), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 5),
        };
        table.Columns.Add(new ColumnDefinition<string>("Name", 10, s => s));
        table.OnInitializeLayout();
        return table;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Table_Click_MovesCursorToRow()
    {
        Table<string> table = CreateTable("a", "b", "c");
        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 3), LeftClicked = true });
        Assert.AreEqual(2, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Table_ClickOnCursorRow_ActivatesIt()
    {
        Table<string> table = CreateTable("a", "b");
        string? activated = null;
        table.RowActivated += (_, row) => activated = row;
        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 1), LeftClicked = true });
        Assert.AreEqual("a", activated);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Table_ClickOnHeader_IsIgnored()
    {
        Table<string> table = CreateTable("a", "b");
        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });
        Assert.AreEqual(0, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Table_Wheel_MovesCursor()
    {
        Table<string> table = CreateTable("a", "b", "c", "d", "e");
        await table.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 2), WheelDelta = -1 });
        Assert.AreEqual(3, table.CursorRow);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Placeholder_ShownWhileEmpty()
    {
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            ViewSize = new Size(30, 1),
            Placeholder = "host:port",
        };
        box.ScrollableModel.ViewSize = new Size(26, 1);
        MockBuffer buffer = new(new Size(30, 1));
        box.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("host:port"));

        box.Text = "x";
        MockBuffer typed = new(new Size(30, 1));
        box.Draw(typed);
        Assert.IsFalse(typed.ContentAsString.Contains("host:port"));
    }
}
