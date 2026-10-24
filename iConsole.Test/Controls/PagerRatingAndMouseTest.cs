using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class PagerRatingAndMouseTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    [TestMethod]
    public void Pagination_PageCount_RoundsUpAndHasOneEmptyPage()
    {
        Pagination pager = new(Theme()) { PageSize = 10, TotalItems = 25 };
        Assert.AreEqual(3, pager.PageCount);
        pager.TotalItems = 0;
        Assert.AreEqual(1, pager.PageCount);
    }

    [TestMethod]
    public void Pagination_Page_IsClampedAndRaisesEvent()
    {
        Pagination pager = new(Theme()) { PageSize = 10, TotalItems = 25 };
        int raised = 0;
        pager.PageChanged += (_, _) => raised++;
        pager.Page = 9;
        Assert.AreEqual(2, pager.Page);
        Assert.AreEqual(20, pager.FirstItemIndex);
        pager.Page = 9;
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void Pagination_ShrinkingTotal_PullsPageBack()
    {
        Pagination pager = new(Theme()) { PageSize = 10, TotalItems = 50 };
        pager.Page = 4;
        pager.TotalItems = 15;
        Assert.AreEqual(1, pager.Page);
    }

    [TestMethod]
    public async Task Pagination_ClickArrows_StepPages()
    {
        Pagination pager = new(Theme()) { PageSize = 10, TotalItems = 30, Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        long right = pager.Caption.Length + 3;
        await pager.HandleMouseEventAsync(Click(right, 0));
        Assert.AreEqual(1, pager.Page);
        await pager.HandleMouseEventAsync(Click(0, 0));
        Assert.AreEqual(0, pager.Page);
    }

    [TestMethod]
    public void Pagination_Draw_ShowsCaption()
    {
        MockBuffer buffer = new(new Size(30, 1));
        new Pagination(Theme()) { PageSize = 10, TotalItems = 30, Position = new Position(0, 0), ViewSize = new Size(30, 1) }.Draw(buffer);
        Assert.AreEqual("‹ Page 1 of 3 ›", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
    }

    [TestMethod]
    public async Task Rating_ClickStar_SetsValueAndClickAgainClears()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0) };
        await rating.HandleMouseEventAsync(Click(2, 0));
        Assert.AreEqual(3, rating.Value);
        await rating.HandleMouseEventAsync(Click(2, 0));
        Assert.AreEqual(0, rating.Value);
    }

    [TestMethod]
    public void Rating_Draw_ShowsFilledAndEmptyStars()
    {
        MockBuffer buffer = new(new Size(5, 1));
        new Rating(Theme()) { Value = 2, Position = new Position(0, 0) }.Draw(buffer);
        Assert.AreEqual("★★☆☆☆", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
    }

    [TestMethod]
    public void Rating_Value_IsClamped()
    {
        Rating rating = new(Theme()) { Maximum = 3 };
        rating.Value = 9;
        Assert.AreEqual(3, rating.Value);
    }

    private static (TreeView Tree, HierarchicalItemModel A, HierarchicalItemModel B) CreateTree()
    {
        HierarchicalItemModel root = new(null, "/") { IsOpened = true };
        HierarchicalItemModel a = new(root, "A");
        a.Children.Add(new HierarchicalItemModel(a, "A1"));
        HierarchicalItemModel b = new(root, "B");
        root.Children.Add(a);
        root.Children.Add(b);
        root.IsFocused = true;
        TreeView tree = new(root, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        return (tree, a, b);
    }

    [TestMethod]
    public async Task TreeView_ClickRow_MovesFocus()
    {
        (TreeView tree, _, HierarchicalItemModel b) = CreateTree();
        await tree.HandleMouseEventAsync(Click(3, 2));
        Assert.IsTrue(b.IsFocused);
        Assert.AreSame(b, tree.GetFocusedItem());
    }

    [TestMethod]
    public async Task TreeView_ClickGlyph_TogglesNode()
    {
        (TreeView tree, HierarchicalItemModel a, _) = CreateTree();
        await tree.HandleMouseEventAsync(Click(2, 1));
        Assert.IsTrue(a.IsOpened);
    }

    [TestMethod]
    public async Task TreeView_ClickFocusedRowAgain_TogglesNode()
    {
        (TreeView tree, HierarchicalItemModel a, _) = CreateTree();
        await tree.HandleMouseEventAsync(Click(5, 1));
        Assert.IsFalse(a.IsOpened);
        await tree.HandleMouseEventAsync(Click(5, 1));
        Assert.IsTrue(a.IsOpened);
    }

    [TestMethod]
    public async Task TextBox_Click_PlacesCursor()
    {
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 1),
            Text = "hello world",
        };
        box.ScrollableModel.ViewSize = new Size(26, 1);
        await box.HandleMouseEventAsync(Click(1 + 3, 0));
        Assert.AreEqual(3, (int)box.ScrollableModel.CursorPosition.X);
        await box.HandleMouseEventAsync(Click(29, 0));
        Assert.AreEqual(11, (int)box.ScrollableModel.CursorPosition.X);
    }
}
