using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Controls.Lenses;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

[TestClass]
public class ContentBrowserTest
{
    private sealed class TestContentModel(string name, IFrequentExecutionGuardService frequentExecutionGuardService)
        : ContentModelBase(name, frequentExecutionGuardService)
    {
        public override bool IsReadOnly => false;

        public override string GetFooter() => string.Empty;

        public override string GetHeader(int width) => Name;

        public override void ReadUrl()
        {
        }

        public override void Refresh(bool forceRefresh = false)
        {
        }

        public override void SetUrl(Url url)
        {
            Url = url;
        }
    }

    private sealed class TestLens(string indicator) : IContentLens
    {
        public string? GetHeaderIndicator(IContentModel contentModel)
        {
            _ = contentModel;
            return indicator;
        }

        public void DecorateRow(
            IContentModel contentModel,
            IEntry? entry,
            ConxelString row,
            bool cursor,
            ColorScheme colorScheme,
            VisualState visualState)
        {
            _ = contentModel;
            _ = entry;
            _ = row;
            _ = cursor;
            _ = colorScheme;
            _ = visualState;
        }
    }

    private static ContentBrowser CreateSut(
        string name,
        IContentModelService contentModelService,
        IThemeService themeService)
    {
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(
            MockBehavior.Loose,
            taskManager,
            contentModelService).Object;

        return new ContentBrowser(
            name,
            contentModelService,
            terminalModel,
            taskManager,
            themeService);
    }

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static TestContentModel CreateTestContentModel(string rootPath, params string[] fileNames)
    {
        var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
        frequentExecutionGuardService
            .Setup(service => service.Execute(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        var model = new TestContentModel("Panel", frequentExecutionGuardService.Object)
        {
            ViewSize = new Size(38, 10)
        };

        model.SetUrl(new Url(rootPath));
        foreach (var fileName in fileNames)
        {
            model.Entries.Add(new FileSystemEntry(fileName, Path.Combine(rootPath, fileName)));
        }

        model.Update();
        return model;
    }

    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();

        var sut = CreateSut("Panel1", contentModelService, themeService);

        Assert.IsNotNull(sut);
    }

    [TestMethod]
    public void Constructor_SetsNameCorrectly()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();

        var sut = CreateSut("LeftPanel", contentModelService, themeService);

        Assert.AreEqual("LeftPanel", sut.Name);
    }

    [TestMethod]
    public void Constructor_StoresContentModelService()
    {
        var mockService = new Mock<IContentModelService>();
        var themeService = CreateThemeService();

        var sut = CreateSut("Panel", mockService.Object, themeService);

        Assert.AreSame(mockService.Object, sut.ContentModelService);
    }

    [TestMethod]
    public void Constructor_ContentModelIsNull_Initially()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();

        var sut = CreateSut("Panel", contentModelService, themeService);

        Assert.IsNull(sut.ContentModel);
    }

    [TestMethod]
    public void ContentModel_CanBeAssigned()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);

        var model = new Mock<IContentModel>().Object;
        sut.ContentModel = model;

        Assert.AreSame(model, sut.ContentModel);
    }

    [TestMethod]
    public void Constructor_WithoutDialogs_DoesNotThrow()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(
            MockBehavior.Loose,
            taskManager,
            contentModelService).Object;

        var sut = new ContentBrowser(
            "Panel",
            contentModelService,
            terminalModel,
            taskManager,
            themeService,
            createFolderDialog: null,
            copyDialog: null,
            deleteDialog: null,
            moveDialog: null,
            textFileEditorDialog: null,
            helpDialog: null,
            fileViewerDialog: null);

        Assert.IsNotNull(sut);
    }

    [TestMethod]
    public void Constructor_ViewSize_IsZero_Initially()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();

        var sut = CreateSut("Panel", contentModelService, themeService);

        Assert.AreEqual(new Size(0, 0), sut.ViewSize);
    }

    [TestMethod]
    public void Constructor_CanFocus_IsTrue()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();

        var sut = CreateSut("Panel", contentModelService, themeService);

        Assert.IsTrue(sut.CanFocus);
    }

    [TestMethod]
    public void ModelChanged_SetsHasStateChanged_WhenTriggered()
    {
        var mockService = new Mock<IContentModelService>();
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", mockService.Object, themeService);

        // Reset HasStateChanged to false so we can detect the next change.
        sut.HasStateChanged = false;

        mockService.Raise(s => s.ModelChanged += null, EventArgs.Empty);

        Assert.IsTrue(sut.HasStateChanged);
    }

    [TestMethod]
    public async Task OnInitializeAsync_WithContentModel_SubscribesToPropertyChanged()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);
        // ViewSize must be positive so OnInitializeLayout does not trigger a debug assert.
        sut.ViewSize = new Size(40, 20);

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        model.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        model.SetupProperty(m => m.ViewSize, new Size(0, 0));
        sut.ContentModel = model.Object;

        await sut.OnInitializeAsync();

        // If subscription succeeded, raising PropertyChanged on the model should set HasStateChanged.
        sut.HasStateChanged = false;
        model.Raise(m => m.PropertyChanged += null, new System.ComponentModel.PropertyChangedEventArgs("anything"));

        Assert.IsTrue(sut.HasStateChanged);
    }

    [TestMethod]
    public async Task OnInitializeAsync_WithNullContentModel_ThrowsArgumentNullException()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);
        sut.ViewSize = new Size(40, 20);
        sut.ContentModel = null;

        var threw = false;
        try
        {
            await sut.OnInitializeAsync();
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "Expected ArgumentNullException when ContentModel is null.");
    }

    [TestMethod]
    public async Task HandleMouseEventAsync_WithWheelDelta_UpdatesScrollOnce()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);
        sut.ViewSize = new Size(20, 7);

        var entries = Enumerable
            .Range(0, 20)
            .Select(_ => Mock.Of<IEntry>())
            .ToList();

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        model.SetupProperty(m => m.CursorPosition, new Position(0, 5));
        model.SetupProperty(m => m.ViewSize, new Size(18, 5));
        model.SetupProperty(m => m.ScrollableSize, new Size(18, entries.Count));
        model.SetupGet(m => m.FilteredEntries).Returns(entries);
        sut.ContentModel = model.Object;

        var handled = await sut.HandleMouseEventAsync(new MouseEvent
        {
            Position = new Position(2, 2),
            WheelDelta = -3
        });

        Assert.IsTrue(handled);
        Assert.AreEqual(8, model.Object.CursorPosition.Y);
        Assert.AreEqual(4, model.Object.ScrollPosition.Y);
        model.Verify(m => m.Update(), Times.Once);
    }

    [TestMethod]
    public async Task FilePanelScroll_WhenRenderedClean_IdleDoesNotInvalidatePanelAgain()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);
        sut.ViewSize = new Size(40, 12);

        var entries = Enumerable
            .Range(0, 1000)
            .Select(_ => Mock.Of<IEntry>())
            .ToList();

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        model.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        model.SetupProperty(m => m.ViewSize, new Size(38, 10));
        model.SetupProperty(m => m.ScrollableSize, new Size(38, entries.Count));
        model.SetupGet(m => m.FilteredEntries).Returns(entries);
        sut.ContentModel = model.Object;
        await sut.OnInitializeAsync();

        var handled = await sut.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.IsTrue(handled);
        Assert.IsTrue(sut.HasStateChanged);

        sut.HasStateChanged = false;
        sut.OnIdle();

        Assert.IsFalse(sut.HasStateChanged, "Idle must not force the file panel back into a redraw loop after scrolling.");
    }

    [TestMethod]
    public void OnInitializeLayout_WithParentAndModel_SetsModelViewSize()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.SetupProperty(m => m.ViewSize, new Size(0, 0));
        sut.ContentModel = model.Object;

        // Attach a parent so the layout branch executes.
        var parent = new iConsole.Controls.Window(themeService);
        parent.AddControl(sut);

        sut.ViewSize = new Size(40, 20);
        sut.OnInitializeLayout();

        // ContentModel.ViewSize should equal (Width-2) x (Height-2)
        Assert.AreEqual(38u, model.Object.ViewSize.Width);
        Assert.AreEqual(18u, model.Object.ViewSize.Height);
    }

    [TestMethod]
    public void OnInitializeLayout_WithoutParent_DoesNotThrow()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);

        sut.ViewSize = new Size(40, 20);

        // Should not throw even without parent or model
        sut.OnInitializeLayout();
    }

    [TestMethod]
    public void Draw_WhenRowsAreFullyPopulated_DoesNotFormatBlankRows()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);
        sut.ViewSize = new Size(20, 5);

        var entries = Enumerable
            .Range(0, 3)
            .Select(_ => Mock.Of<IEntry>())
            .ToList();

        var model = new Mock<IContentModel>(MockBehavior.Strict);
        model.SetupGet(m => m.FilteredEntries).Returns(entries);
        model.SetupGet(m => m.ScrollPosition).Returns(new Position(0, 0));
        model.SetupGet(m => m.CursorPosition).Returns(new Position(0, 1));
        model.Setup(m => m.GetHeader(It.IsAny<int>())).Returns("Panel");
        model.Setup(m => m.GetFooter()).Returns(string.Empty);
        model.Setup(m => m.FormatItemName(entries[0], 20, false, It.IsAny<ColorScheme>(), It.IsAny<VisualState>())).Returns(new ConxelString("row0", Color.White, Color.Black));
        model.Setup(m => m.FormatItemName(entries[1], 20, true, It.IsAny<ColorScheme>(), It.IsAny<VisualState>())).Returns(new ConxelString("row1", Color.White, Color.Black));
        model.Setup(m => m.FormatItemName(entries[2], 20, false, It.IsAny<ColorScheme>(), It.IsAny<VisualState>())).Returns(new ConxelString("row2", Color.White, Color.Black));

        sut.ContentModel = model.Object;

        var buffer = new MockBuffer(new Size(40, 10));
        sut.Draw(buffer);

        model.Verify(m => m.FormatItemName(null, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<ColorScheme>(), It.IsAny<VisualState>()), Times.Never);
    }

    [TestMethod]
    public void Draw_WhenRowsAreMissing_FormatsBlankRowsForTheRemainder()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var sut = CreateSut("Panel", contentModelService, themeService);
        sut.ViewSize = new Size(20, 5);

        var entries = Enumerable
            .Range(0, 1)
            .Select(_ => Mock.Of<IEntry>())
            .ToList();

        var model = new Mock<IContentModel>(MockBehavior.Strict);
        model.SetupGet(m => m.FilteredEntries).Returns(entries);
        model.SetupGet(m => m.ScrollPosition).Returns(new Position(0, 0));
        model.SetupGet(m => m.CursorPosition).Returns(new Position(0, 0));
        model.Setup(m => m.GetHeader(It.IsAny<int>())).Returns("Panel");
        model.Setup(m => m.GetFooter()).Returns(string.Empty);
        model.Setup(m => m.FormatItemName(entries[0], 20, true, It.IsAny<ColorScheme>(), It.IsAny<VisualState>())).Returns(new ConxelString("row0", Color.White, Color.Black));
        model.Setup(m => m.FormatItemName(null, 20, false, It.IsAny<ColorScheme>(), It.IsAny<VisualState>())).Returns(new ConxelString("blank", Color.White, Color.Black));

        sut.ContentModel = model.Object;

        var buffer = new MockBuffer(new Size(40, 10));
        sut.Draw(buffer);

        model.Verify(m => m.FormatItemName(null, 20, false, It.IsAny<ColorScheme>(), It.IsAny<VisualState>()), Times.Exactly(2));
    }

    [TestMethod]
    public void Draw_WithLensIndicator_IncludesIndicatorInHeader()
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var themeService = CreateThemeService();
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(
            MockBehavior.Loose,
            taskManager,
            contentModelService).Object;

        var sut = new ContentBrowser(
            "Panel",
            contentModelService,
            terminalModel,
            taskManager,
            themeService,
            contentLenses: [new TestLens("{Git}")]);

        sut.ViewSize = new Size(30, 5);

        var model = new Mock<IContentModel>(MockBehavior.Strict);
        model.SetupGet(m => m.FilteredEntries).Returns([]);
        model.SetupGet(m => m.ScrollPosition).Returns(new Position(0, 0));
        model.SetupGet(m => m.CursorPosition).Returns(new Position(0, 0));
        model.Setup(m => m.GetHeader(It.IsAny<int>())).Returns("Panel");
        model.Setup(m => m.GetFooter()).Returns(string.Empty);
        model.Setup(m => m.FormatItemName(null, 30, false, It.IsAny<ColorScheme>(), It.IsAny<VisualState>())).Returns(new ConxelString("blank", Color.White, Color.Black));

        sut.ContentModel = model.Object;

        var buffer = new MockBuffer(new Size(50, 10));
        sut.Draw(buffer);

        Assert.IsTrue(buffer.Content.TryGetValue(0, out var headerLine));
        Assert.IsNotNull(headerLine);
        Assert.IsTrue(headerLine.Contains("{Git}", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_WithAltCharacters_KeepsAllEntriesVisibleAndMovesToFirstMatch()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var contentModelService = new Mock<IContentModelService>().Object;
            var themeService = CreateThemeService();
            var sut = CreateSut("Panel", contentModelService, themeService);
            var model = CreateTestContentModel(rootPath, "alpha.txt", "beta.txt", "gamma.txt");
            model.CursorPosition.Y = 0;
            model.Update();
            sut.ContentModel = model;

            // First character requires ALT to start the search.
            var handledB = await sut.HandleKeyEventAsync(new KeyEvent('b', ConsoleKey.B, false, true, false));
            // Subsequent characters extend without ALT.
            var handledE = await sut.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false));

            Assert.IsTrue(handledB);
            Assert.IsTrue(handledE);
            Assert.AreEqual("be", model.Filter);
            Assert.AreEqual(3, model.FilteredEntries.Count);
            Assert.AreEqual("beta.txt", model.GetFocusedEntry()?.Url.DisplayName);
            Assert.AreEqual(1, model.CursorPosition.Y);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_WithEscape_ClearsFilterAndKeepsCurrentFocusedEntry()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var contentModelService = new Mock<IContentModelService>().Object;
            var themeService = CreateThemeService();
            var sut = CreateSut("Panel", contentModelService, themeService);
            var model = CreateTestContentModel(rootPath, "alpha.txt", "beta.txt", "gamma.txt");
            model.CursorPosition.Y = 2;
            model.Update();
            sut.ContentModel = model;

            // First key with ALT starts search; second key plain extends it.
            await sut.HandleKeyEventAsync(new KeyEvent('b', ConsoleKey.B, false, true, false));
            await sut.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false));

            // Search moved cursor to "beta.txt" (first match for "be").
            Assert.AreEqual("beta.txt", model.GetFocusedEntry()?.Url.DisplayName);

            var handled = await sut.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

            Assert.IsTrue(handled);
            Assert.IsNull(model.Filter);
            // Cursor must stay on the item that was focused when Escape was pressed.
            Assert.AreEqual("beta.txt", model.GetFocusedEntry()?.Url.DisplayName);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_WithoutAltAfterSearchStarted_ExtendsFilter()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var contentModelService = new Mock<IContentModelService>().Object;
            var themeService = CreateThemeService();
            var sut = CreateSut("Panel", contentModelService, themeService);
            var model = CreateTestContentModel(rootPath, "alpha.txt", "beta.txt", "gamma.txt");
            model.Update();
            sut.ContentModel = model;

            // Plain key without ALT must NOT start a search when filter is null.
            var notHandled = await sut.HandleKeyEventAsync(new KeyEvent('b', ConsoleKey.B, false, false, false));
            Assert.IsNull(model.Filter, "Plain key should not start a search.");

            // ALT + key starts the search.
            await sut.HandleKeyEventAsync(new KeyEvent('b', ConsoleKey.B, false, true, false));
            Assert.AreEqual("b", model.Filter);

            // Plain keys extend the search without ALT.
            await sut.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false));
            await sut.HandleKeyEventAsync(new KeyEvent('t', ConsoleKey.T, false, false, false));
            Assert.AreEqual("bet", model.Filter);
            Assert.AreEqual("beta.txt", model.GetFocusedEntry()?.Url.DisplayName);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FormatItemName_WithFilter_NonMatchingEntryUsesLighterBackground()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var model = CreateTestContentModel(rootPath, "alpha.txt", "beta.txt");
            model.Filter = "be";
            model.Update();

            var colorScheme = new ColorScheme { Background = Color.Black, Foreground = Color.White };
            var alphaEntry = model.FilteredEntries.Single(entry => entry.Url.DisplayName == "alpha.txt");
            var betaEntry = model.FilteredEntries.Single(entry => entry.Url.DisplayName == "beta.txt");

            var alphaText = model.FormatItemName(alphaEntry, 40, false, colorScheme, VisualState.Focused);
            var betaText = model.FormatItemName(betaEntry, 40, false, colorScheme, VisualState.Focused);

            Assert.IsTrue(alphaText.String.All(cell => cell.BackgroundColor == Color.Blue));
            Assert.IsFalse(betaText.String.All(cell => cell.BackgroundColor == Color.Blue));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
