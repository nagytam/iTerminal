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

    [TestMethod]
    public void Draw_SingleItem_RendersItem()
    {
        var theme = CreateTheme();
        var buffer = new MockBuffer(new Size(20, 3));
        var listBox = CreateListBox(theme.Object, ["Alpha"], new Size(20, 3));

        listBox.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Alpha"));
    }

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

    [TestMethod]
    public void GetVisualState_CursorRow_ReturnsSelected()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["A", "B"], new Size(10, 2));

        var state = listBox.GetVisualState(0);

        Assert.AreEqual(VisualState.Selected, state);
    }

    [TestMethod]
    public void GetVisualState_NonCursorRow_ReturnsNormal()
    {
        var theme = CreateTheme();
        var listBox = CreateListBox(theme.Object, ["A", "B"], new Size(10, 2));

        var state = listBox.GetVisualState(1);

        Assert.AreEqual(VisualState.Normal, state);
    }
}
