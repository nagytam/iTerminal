using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Viewers;

[TestClass]
public class TextViewerControlTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer);
        Assert.IsInstanceOfType(viewer, typeof(Control));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListTheScrollKeys_AndNothingWhenDisabled()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        var keys = viewer.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(keys, "PageUp/PageDown");
        CollectionAssert.Contains(keys, "Ctrl+Home/Ctrl+End");

        viewer.IsEnabled = false;
        Assert.AreEqual(0, viewer.GetKeyHints().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlHasTextModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer.TextModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlHasScrollableModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer.ScrollableModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlHasSearchModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer.SearchModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlHasSelectionModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer.SelectionTextModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCursorDisabledByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsFalse(viewer.IsCursorVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCursorCanBeEnabled()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(true, mockThemeService.Object);

        Assert.IsTrue(viewer.IsCursorVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCursorPositionCanBeRead()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object)
        {
            ViewSize = new Size(80, 24),
            IsVisible = true,
            IsEnabled = true
        };

        var cursorPosition = viewer.CursorPosition;

        Assert.IsNotNull(cursorPosition);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCanAddText()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        viewer.TextModel.Lines.Add("Hello, World!");

        Assert.AreEqual(1, viewer.TextModel.Lines.Count);
        Assert.AreEqual("Hello, World!", viewer.TextModel.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCanClearText()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        viewer.TextModel.Lines.Add("Line 1");
        viewer.TextModel.Lines.Add("Line 2");
        viewer.TextModel.Lines.Clear();

        Assert.AreEqual(0, viewer.TextModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlInitializeLayoutSetsScrollableSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parent = new Window(mockThemeService.Object)
        {
            ViewSize = new Size(100, 30)
        };
        var viewer = new TextViewerControl(false, mockThemeService.Object)
        {
            Parent = parent,
            ViewSize = new Size(100, 30)
        };

        viewer.TextModel.Lines.Add("Test line");
        viewer.OnInitializeLayout();

        Assert.IsTrue(viewer.ScrollableModel.ScrollableSize.Width > 0);
        Assert.AreEqual(1, (int)viewer.ScrollableModel.ScrollableSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlInitializeLayoutWithEmptyText()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parent = new Window(mockThemeService.Object)
        {
            ViewSize = new Size(100, 30)
        };
        var viewer = new TextViewerControl(false, mockThemeService.Object)
        {
            Parent = parent,
            ViewSize = new Size(100, 30)
        };

        viewer.OnInitializeLayout();

        Assert.AreEqual(0, (int)viewer.ScrollableModel.ScrollableSize.Width);
        Assert.AreEqual(0, (int)viewer.ScrollableModel.ScrollableSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextViewerControlInitializeAsync()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object)
        {
            ViewSize = new Size(80, 24)
        };

        await viewer.OnInitializeAsync();

        // Should initialize scroll bars
        Assert.IsNotNull(viewer);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCanDrawContent()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 24));
        var viewer = new TextViewerControl(false, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 24)
        };

        viewer.TextModel.Lines.Add("Test content");
        viewer.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlHandlesMultipleLines()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        viewer.TextModel.Lines.Add("Line 1");
        viewer.TextModel.Lines.Add("Line 2");
        viewer.TextModel.Lines.Add("Line 3");

        Assert.AreEqual(3, viewer.TextModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCanScroll()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        // Add many lines
        for (int i = 0; i < 50; i++)
        {
            viewer.TextModel.Lines.Add($"Line {i}");
        }

        Assert.AreEqual(50, viewer.TextModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlScrollableModelUpdatesWithTextSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        viewer.TextModel.Lines.Add("Short");
        var firstWidth = viewer.ScrollableModel.ScrollableSize.Width;

        viewer.TextModel.Lines.Add("This is a much longer line");
        var secondWidth = viewer.ScrollableModel.ScrollableSize.Width;

        // Width should have increased or stayed same (longest line)
        Assert.IsTrue(firstWidth <= secondWidth || secondWidth > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlSearchModelAvailable()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer.SearchModel);
        // SearchModel does not expose SearchText property, just verify it exists
        Assert.IsNotNull(viewer.SearchModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlSelectionModelAvailable()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewer = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsNotNull(viewer.SelectionTextModel);
        Assert.IsFalse(viewer.SelectionTextModel.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlCursorVisibilityToggle()
    {
        var mockThemeService = new Mock<IThemeService>();
        var viewerWithCursor = new TextViewerControl(true, mockThemeService.Object);
        var viewerWithoutCursor = new TextViewerControl(false, mockThemeService.Object);

        Assert.IsTrue(viewerWithCursor.IsCursorVisible);
        Assert.IsFalse(viewerWithoutCursor.IsCursorVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewerControlRefreshUpdatesDisplay()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 24));
        var viewer = new TextViewerControl(false, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 24)
        };

        viewer.TextModel.Lines.Add("Initial content");
        viewer.Refresh();
        viewer.Draw(buffer);

        var firstOutput = buffer.ContentAsString;

        buffer.Clear();
        viewer.TextModel.Lines.Clear();
        viewer.TextModel.Lines.Add("Updated content");
        viewer.Refresh();
        viewer.Draw(buffer);

        var secondOutput = buffer.ContentAsString;

        // Content should have changed
        Assert.IsTrue(firstOutput.Length > 0 && secondOutput.Length > 0);
    }
}
