using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

[TestClass]
public class TextEditorControlTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsNotNull(editor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlHasTextModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsNotNull(editor.TextModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlHasScrollableModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsNotNull(editor.ScrollableModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlHasSearchModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsNotNull(editor.SearchModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlHasSelectionModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsNotNull(editor.SelectionTextModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlImplementsIDirtyModel()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsInstanceOfType(editor, typeof(IDirtyModel));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlImplementsIReadOnly()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsInstanceOfType(editor, typeof(IReadOnly));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlIsDirtyPropertyDefaultsToFalse()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsFalse(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlIsDirtyCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.IsDirty = true;
        Assert.IsTrue(editor.IsDirty);

        editor.IsDirty = false;
        Assert.IsFalse(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlIsReadOnlyPropertyDefaultsToFalse()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsFalse(editor.IsReadOnly);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlIsReadOnlyCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.IsReadOnly = true;
        Assert.IsTrue(editor.IsReadOnly);

        editor.IsReadOnly = false;
        Assert.IsFalse(editor.IsReadOnly);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlIsCursorVisibleByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsTrue(editor.IsCursorVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlCanAddText()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.TextModel.Lines.Add("Hello, World!");

        Assert.AreEqual(1, editor.TextModel.Lines.Count);
        Assert.AreEqual("Hello, World!", editor.TextModel.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlCanClearText()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.TextModel.Lines.Add("Line 1");
        editor.TextModel.Lines.Add("Line 2");
        editor.TextModel.Lines.Clear();

        Assert.AreEqual(0, editor.TextModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlRefreshUpdatesScrollableSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.TextModel.Lines.Add("Test line");
        editor.Refresh();

        Assert.IsTrue(editor.ScrollableModel.ScrollableSize.Width > 0);
        Assert.AreEqual(1, (int)editor.ScrollableModel.ScrollableSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlRefreshWithEmptyText()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.Refresh();

        Assert.AreEqual(0, (int)editor.ScrollableModel.ScrollableSize.Width);
        Assert.AreEqual(0, (int)editor.ScrollableModel.ScrollableSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlRaisesPropertyChangedForDirty()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);
        var propertyChanged = false;

        editor.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(editor.IsDirty))
            {
                propertyChanged = true;
            }
        };

        editor.IsDirty = true;

        Assert.IsTrue(propertyChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlRaisesPropertyChangedForReadOnly()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);
        var propertyChanged = false;

        editor.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(editor.IsReadOnly))
            {
                propertyChanged = true;
            }
        };

        editor.IsReadOnly = true;

        Assert.IsTrue(propertyChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextEditorControlInitializeAsync()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object)
        {
            ViewSize = new Size(80, 24)
        };

        await editor.OnInitializeAsync();

        // Should initialize scroll bars
        Assert.IsNotNull(editor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlCanDrawWithContent()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 24));
        var editor = new TextEditorControl(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 24)
        };

        editor.TextModel.Lines.Add("Test content");
        editor.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlHandlesMultipleLines()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        editor.TextModel.Lines.Add("Line 1");
        editor.TextModel.Lines.Add("Line 2");
        editor.TextModel.Lines.Add("Line 3");

        Assert.AreEqual(3, editor.TextModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextEditorControlInheritsFromTextViewerControl()
    {
        var mockThemeService = new Mock<IThemeService>();
        var editor = new TextEditorControl(mockThemeService.Object);

        Assert.IsInstanceOfType(editor, typeof(TextViewerControl));
    }
}
