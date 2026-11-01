using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ListBoxTest
{
    private static Mock<IThemeService> CreateTheme()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock;
    }

    private static ListBox<string> CreateListBox(IThemeService theme, IEnumerable<string> items, Size size)
    {
        var collection = new ConcurrentObservableCollection<string>(items);
        var model = new ListBoxModel<string>(collection);
        model.UpdateListItems();
        var listBox = new ListBox<string>(model, theme)
        {
            Position = new Position(0, 0),
            ViewSize = size
        };
        listBox.OnInitializeLayout();
        return listBox;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListNavigationAndSelectWhenItemsExist()
    {
        var listBox = CreateListBox(CreateTheme().Object, ["Alpha", "Beta"], new Size(20, 3));

        var keys = listBox.GetKeyHints().Select(h => h.Key).ToList();

        CollectionAssert.AreEqual(new[] { "Up/Down", "PageUp/PageDown", "Home/End", "Enter", "Insert" }, keys);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListTypeAheadOnlyWhenEnabled()
    {
        var listBox = CreateListBox(CreateTheme().Object, ["Alpha"], new Size(20, 3));
        Assert.IsFalse(listBox.GetKeyHints().Any(h => h.Key == "Letters"));

        listBox.EnableTypeAhead = true;

        Assert.IsTrue(listBox.GetKeyHints().Any(h => h.Key == "Letters"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_EmptyWithoutItemsOrWhenDisabled()
    {
        var empty = CreateListBox(CreateTheme().Object, [], new Size(20, 3));
        Assert.AreEqual(0, empty.GetKeyHints().Count());

        var disabled = CreateListBox(CreateTheme().Object, ["Alpha"], new Size(20, 3));
        disabled.IsEnabled = false;
        Assert.AreEqual(0, disabled.GetKeyHints().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_SingleItem_RendersItem()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(20, 3));
        var listBox = CreateListBox(theme.Object, ["Alpha"], new Size(20, 3));

        listBox.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Alpha"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_MultipleItems_RendersAllVisible()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(20, 3));
        var listBox = CreateListBox(theme.Object, ["First", "Second", "Third"], new Size(20, 3));

        listBox.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("First"));
        Assert.IsTrue(buffer.ContentAsString.Contains("Second"));
        Assert.IsTrue(buffer.ContentAsString.Contains("Third"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ItemsExceedViewHeight_OnlyVisibleItemsRendered()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(20, 2));
        var listBox = CreateListBox(theme.Object, ["Row0", "Row1", "Row2"], new Size(20, 2));

        listBox.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("Row0"));
        Assert.IsTrue(content.Contains("Row1"));
        Assert.IsFalse(content.Contains("Row2"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_EmptyModel_PaintsBlankRows()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(20, 3));
        var listBox = CreateListBox(theme.Object, [], new Size(20, 3));

        listBox.Draw(buffer);

        // Blank, but painted: an emptied list must erase the rows it used to show.
        var lines = buffer.ContentAsString.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(3, lines.Length);
        Assert.IsTrue(lines.All(line => line == new string(' ', 20)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ItemIsPaddedToViewWidth()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(10, 1));
        var listBox = CreateListBox(theme.Object, ["Hi"], new Size(10, 1));

        listBox.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(10, lines[0].Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_LongItem_TruncatedToViewWidth()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(5, 1));
        var listBox = CreateListBox(theme.Object, ["TooLongItem"], new Size(5, 1));

        listBox.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual(5, lines[0].Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ItemAtPosition_RenderedAtCorrectRow()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(10, 3));
        var listBox = CreateListBox(theme.Object, ["One", "Two", "Three"], new Size(10, 3));

        listBox.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].Contains("One"));
        Assert.IsTrue(lines[1].Contains("Two"));
        Assert.IsTrue(lines[2].Contains("Three"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetVisualState_CursorRow_ReturnsSelected()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["A", "B"], new Size(10, 2));

        var state = listBox.GetVisualState(0);

        Assert.AreEqual(VisualState.Selected, state);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetVisualState_NonCursorRow_ReturnsNormal()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["A", "B"], new Size(10, 2));

        var state = listBox.GetVisualState(1);

        Assert.AreEqual(VisualState.Normal, state);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleMouseEvent_ClickOnRow_MovesCursorAndSelectsItem()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["Alpha", "Beta", "Gamma"], new Size(20, 3));

        var handled = await listBox.HandleMouseEventAsync(new MouseEvent
        {
            Position = new Position(2, 1),
            LeftClicked = true
        });

        Assert.IsTrue(handled);
        Assert.AreEqual(1, listBox.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual("Beta", listBox.SelectedItem?.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleMouseEvent_ClickOnRowBelowLastItem_SelectsNothing()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["Alpha", "Beta"], new Size(20, 4));

        var handled = await listBox.HandleMouseEventAsync(new MouseEvent
        {
            Position = new Position(2, 3),
            LeftClicked = true
        });

        Assert.IsFalse(handled);
        Assert.IsNull(listBox.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleMouseEvent_ClickOnScrolledRow_SelectsTheItemUnderIt()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["R0", "R1", "R2", "R3", "R4"], new Size(20, 2));
        listBox.ScrollableModel.CursorPosition.Y = 4;
        listBox.ScrollableModel.ScrollToVisible();

        var scrolledBy = listBox.ScrollableModel.ScrollPosition.Y;
        await listBox.HandleMouseEventAsync(new MouseEvent
        {
            Position = new Position(0, 0),
            LeftClicked = true
        });

        Assert.AreEqual(scrolledBy, listBox.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual("R" + scrolledBy, listBox.SelectedItem?.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleMouseEvent_Wheel_MovesCursorAndClampsAtTheEnds()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["A", "B", "C", "D", "E"], new Size(20, 2));

        await listBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), WheelDelta = -1 });
        Assert.AreEqual(3, listBox.ScrollableModel.CursorPosition.Y);

        await listBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), WheelDelta = -1 });
        Assert.AreEqual(4, listBox.ScrollableModel.CursorPosition.Y);

        await listBox.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), WheelDelta = 1 });
        Assert.AreEqual(1, listBox.ScrollableModel.CursorPosition.Y);
    }
}
