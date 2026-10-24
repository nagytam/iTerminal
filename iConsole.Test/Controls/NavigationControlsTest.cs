using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class NavigationControlsTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static string FirstLine(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine)[0];

    [TestMethod]
    public void Breadcrumb_Layout_FitsUnchanged()
    {
        List<(int Index, string Text)> parts = Breadcrumb.Layout(["Home", "Projects", "App"], 40);
        Assert.AreEqual(3, parts.Count);
    }

    [TestMethod]
    public void Breadcrumb_Layout_FoldsMiddleKeepingFirstAndLast()
    {
        List<(int Index, string Text)> parts = Breadcrumb.Layout(["Home", "Projects", "Deep", "Folder", "Leaf"], 20);
        Assert.AreEqual(0, parts[0].Index);
        Assert.AreEqual(-1, parts[1].Index);
        Assert.AreEqual(4, parts[^1].Index);
    }

    [TestMethod]
    public void Breadcrumb_Draw_ShowsSeparators()
    {
        MockBuffer buffer = new(new Size(30, 1));
        new Breadcrumb(Theme()) { Segments = ["A", "B"], Position = new Position(0, 0), ViewSize = new Size(30, 1) }.Draw(buffer);
        Assert.AreEqual("A › B", FirstLine(buffer).TrimEnd());
    }

    [TestMethod]
    public async Task Breadcrumb_Click_ActivatesSegment()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["Home", "Docs"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        int activated = -1;
        crumb.SegmentActivated += (_, i) => activated = i;
        await crumb.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), LeftClicked = true });
        Assert.AreEqual(0, activated);
    }

    [TestMethod]
    public void SegmentedControl_Click_SelectsOption()
    {
        SegmentedControl control = new(Theme()) { Options = ["Day", "Week", "Month"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        Assert.AreEqual(1, control.IndexAtColumn(7));
        Assert.AreEqual(-1, control.IndexAtColumn(5));
    }

    [TestMethod]
    public async Task SegmentedControl_MouseClick_ChangesSelection()
    {
        SegmentedControl control = new(Theme()) { Options = ["Day", "Week"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        await control.HandleMouseEventAsync(new MouseEvent { Position = new Position(7, 0), LeftClicked = true });
        Assert.AreEqual("Week", control.SelectedOption);
    }

    [TestMethod]
    public void SegmentedControl_SelectedIndex_IsClamped()
    {
        SegmentedControl control = new(Theme()) { Options = ["A", "B"] };
        control.SelectedIndex = 9;
        Assert.AreEqual(1, control.SelectedIndex);
        control.SelectedIndex = -4;
        Assert.AreEqual(0, control.SelectedIndex);
    }

    [TestMethod]
    public void Expander_Collapsed_TakesOneRowAndDrawsArrow()
    {
        MockBuffer buffer = new(new Size(20, 1));
        new Expander(Theme()) { Header = "More", Position = new Position(0, 0), ViewSize = new Size(20, 1) }.Draw(buffer);
        Assert.IsTrue(FirstLine(buffer).StartsWith("▶ More"));
    }

    [TestMethod]
    public void Expander_Expanding_AttachesContentAndGrows()
    {
        Label content = new("x", Theme());
        Expander expander = new(Theme()) { Header = "More", ContentHeight = 2, Content = content };
        Assert.AreEqual(1, (int)expander.ViewSize.Height);
        Assert.IsFalse(expander.Controls.Contains(content));

        expander.IsExpanded = true;
        Assert.AreEqual(3, (int)expander.ViewSize.Height);
        Assert.IsTrue(expander.Controls.Contains(content));

        expander.IsExpanded = false;
        Assert.AreEqual(1, (int)expander.ViewSize.Height);
        Assert.IsFalse(expander.Controls.Contains(content));
    }

    [TestMethod]
    public async Task Expander_Space_TogglesWhenFocused()
    {
        Expander expander = new(Theme()) { Header = "More" };
        expander.SetFocus();
        await expander.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        Assert.IsTrue(expander.IsExpanded);
    }

    [TestMethod]
    public async Task Expander_PlusOpensAndMinusCloses_WhenFocused()
    {
        Expander expander = new(Theme()) { Header = "More" };
        expander.SetFocus();

        Assert.IsTrue(await expander.HandleKeyEventAsync(new KeyEvent('+', ConsoleKey.Add, false, false, false)));
        Assert.IsTrue(expander.IsExpanded);
        Assert.IsTrue(await expander.HandleKeyEventAsync(new KeyEvent('+', ConsoleKey.Add, false, false, false)));
        Assert.IsTrue(expander.IsExpanded, "plus never closes");

        Assert.IsTrue(await expander.HandleKeyEventAsync(new KeyEvent('-', ConsoleKey.Subtract, false, false, false)));
        Assert.IsFalse(expander.IsExpanded);
    }

    [TestMethod]
    public async Task Expander_PlusMinus_AreIgnoredWithoutFocus()
    {
        Expander expander = new(Theme()) { Header = "More" };

        await expander.HandleKeyEventAsync(new KeyEvent('+', ConsoleKey.Add, false, false, false));

        Assert.IsFalse(expander.IsExpanded);
    }
}
