using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class MarkdownViewerControlTest
{
    private readonly TestThemeService _theme = new();

    private static string Document(int paragraphs) =>
        string.Join("\n\n", Enumerable.Range(1, paragraphs).Select(i => $"Paragraph {i}"));

    private (Control Root, MarkdownViewerControl Viewer) Create(int height, string markdown)
    {
        var root = new Control(_theme) { ViewSize = new Size(30, height) };
        var viewer = new MarkdownViewerControl(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, height) };
        root.AddControl(viewer);
        viewer.Markdown = markdown;
        viewer.SetFocus();
        return (root, viewer);
    }

    [TestMethod]
    public void DrawsTheRenderedText()
    {
        var (root, _) = Create(6, "# Title\n\nSome **bold** text.");

        var screen = ScreenBuffer.Render(root, 30, 6);

        Assert.IsNotNull(screen.Find("Title"));
        Assert.IsNotNull(screen.Find("Some bold text."));
    }

    [TestMethod]
    public async Task DownArrow_ScrollsOneRow_WhenFocused()
    {
        var (_, viewer) = Create(5, Document(20));

        await viewer.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.AreEqual(1, viewer.ScrollOffset);
    }

    [TestMethod]
    public async Task Keys_AreIgnored_WhenNotFocused()
    {
        var (root, viewer) = Create(5, Document(20));
        var other = new Control(_theme) { CanFocus = true };
        root.AddControl(other);
        other.SetFocus();

        var handled = await viewer.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.IsFalse(handled);
        Assert.AreEqual(0, viewer.ScrollOffset);
    }

    [TestMethod]
    public async Task PageDown_End_AndHome_MoveWithinTheDocument()
    {
        var (_, viewer) = Create(5, Document(20));
        var lines = viewer.Lines.Count;

        await viewer.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageDown, false, false, false));
        Assert.AreEqual(4, viewer.ScrollOffset);

        await viewer.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false));
        Assert.AreEqual(lines - 5, viewer.ScrollOffset, "End shows the last screenful, not an empty one.");

        await viewer.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, false));
        Assert.AreEqual(0, viewer.ScrollOffset);
    }

    [TestMethod]
    public async Task ScrollingUpAtTheTop_StaysAtTheTop()
    {
        var (_, viewer) = Create(5, Document(20));

        await viewer.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.AreEqual(0, viewer.ScrollOffset);
    }

    [TestMethod]
    public async Task Wheel_ScrollsThreeRows()
    {
        var (_, viewer) = Create(5, Document(20));

        var handled = await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 2), WheelDelta = -1 });

        Assert.IsTrue(handled);
        Assert.AreEqual(3, viewer.ScrollOffset);
    }

    [TestMethod]
    public void Setting_Markdown_ReturnsToTheTop()
    {
        var (_, viewer) = Create(5, Document(20));
        viewer.ScrollBy(5);

        viewer.Markdown = Document(20);

        Assert.AreEqual(0, viewer.ScrollOffset);
    }
}
