using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs.Search;
using iTerminal.Core.Services.Search;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Search;

/// <summary>F7: the dialog, driven the way a user drives it — open, type, Enter, pick.</summary>
[TestClass]
public class FindFilesDialogTest
{
    private string _root = string.Empty;
    private Mock<IContentModelService> _contentModelService = null!;
    private Mock<IContentModel> _panel = null!;
    private readonly List<(string Name, Url Url)> _navigations = [];

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "findfiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "alpha.txt"), "first\nneedle here\n");
        File.WriteAllText(Path.Combine(_root, "sub", "beta.txt"), "no match\n");
        File.WriteAllText(Path.Combine(_root, "gamma.md"), "needle\n");

        _panel = new Mock<IContentModel>();
        _panel.SetupGet(p => p.Name).Returns("LeftPanel");
        _panel.SetupGet(p => p.Url).Returns(new Url(_root + Path.DirectorySeparatorChar));

        _contentModelService = new Mock<IContentModelService>();
        // A dialog that is open has taken focus from the panel: only the last-focused one is known.
        _contentModelService.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);
        _contentModelService.Setup(s => s.GetLastContentModel()).Returns(_panel.Object);
        _contentModelService.SetupGet(s => s.ContentModels).Returns([_panel.Object]);
        _contentModelService.Setup(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()))
            .Callback<string, Url>((name, url) => _navigations.Add((name, url)))
            .Returns(Task.CompletedTask);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private FindFilesDialog CreateDialog(FileSearchService service)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        return new FindFilesDialog(service, _contentModelService.Object, Mock.Of<IBeepService>(), dateTime.Object, TestTheme());
    }

    private static async Task SearchForAsync(FindFilesDialog dialog, FileSearchService service, string names, int expected)
    {
        dialog.NamesBoxForTest.Text = names;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await service.SearchTask;
        dialog.OnIdle();
        await WaitUntilAsync(() => dialog.ResultsForTest.Count == expected, $"{expected} results for {names}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ItOpensOnTheFocusedPanelsFolder()
    {
        var dialog = CreateDialog(new FileSearchService());
        await ShowAsync(dialog);

        Assert.AreEqual(_root, dialog.FolderBoxForTest.Text);
        Assert.IsTrue(dialog.NamesBoxForTest.IsFocused, "typing starts in the name box");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterSearchesAndTheResultsAreListedAndDrawn()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        var root = await ShowAsync(dialog);

        await SearchForAsync(dialog, service, "*.txt", 2);

        var screen = Render(root);
        Assert.IsTrue(screen.Lines.Any(l => l.Contains("alpha.txt")), screen.ToString());
        Assert.IsTrue(screen.Lines.Any(l => l.Contains(Path.Combine("sub", "beta.txt"))), "results are shown relative to the start folder");
        Assert.IsTrue(screen.Lines.Any(l => l.Contains("Results (2)")));
        Assert.IsTrue(dialog.ResultListBoxForTest.IsFocused, "the results take focus once a search starts");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AContentSearchShowsTheMatchingLine()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        var root = await ShowAsync(dialog);
        dialog.TextBoxForTest.Text = "needle";

        await SearchForAsync(dialog, service, "*.txt", 1);

        Assert.IsTrue(Render(root).Lines.Any(l => l.Contains("alpha.txt:2: needle here")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAResultGoesToItsFolderAndCloses()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        var root = await ShowAsync(dialog);
        await SearchForAsync(dialog, service, "gamma", 1);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        var (name, url) = _navigations.Single();
        Assert.AreEqual("LeftPanel", name);
        Assert.AreEqual(_root, url.FullName.TrimEnd(Path.DirectorySeparatorChar));
        _panel.Verify(p => p.RestoreNavigation(It.Is<string>(u => u.EndsWith("gamma.md")), 0, 0), "the cursor lands on the file");
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlPPutsTheResultsInThePanel()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        var root = await ShowAsync(dialog);
        await SearchForAsync(dialog, service, "*.md", 1);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.P, 'p', control: true));

        Assert.AreEqual(Container.SEARCH_RESULTS, _navigations.Single().Url.FullUrl);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
        Assert.AreEqual(1, service.Hits.Count, "the results outlive the dialog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderThatDoesNotExistIsRefusedWithAMessage()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        dialog.FolderBoxForTest.Text = Path.Combine(_root, "missing");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        StringAssert.StartsWith(dialog.MessageForTest, "No such folder");
        Assert.IsNull(service.Query, "nothing was started");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReopeningKeepsTheLastWordsButTakesThePanelsFolder()
    {
        var service = new FileSearchService();
        var first = CreateDialog(service);
        await ShowAsync(first);
        await SearchForAsync(first, service, "*.md", 1);
        first.Close();

        var second = CreateDialog(service);
        await ShowAsync(second);

        Assert.AreEqual("*.md", second.NamesBoxForTest.Text);
        Assert.AreEqual(_root, second.FolderBoxForTest.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlEndAndCtrlHome_JumpToTheLastAndFirstResult_FromTheNameBox()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SearchForAsync(dialog, service, "*.txt", 2);
        dialog.NamesBoxForTest.SetFocus();

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true)));
        Assert.AreEqual(1, (int)dialog.ResultListBoxForTest.ScrollableModel.CursorPosition.Y);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, true)));
        Assert.AreEqual(0, (int)dialog.ResultListBoxForTest.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PlainHomeAndEnd_StayWithTheTextBoxCursor()
    {
        var service = new FileSearchService();
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SearchForAsync(dialog, service, "*.txt", 2);
        dialog.NamesBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End, '\0'));

        Assert.AreEqual(0, (int)dialog.ResultListBoxForTest.ScrollableModel.CursorPosition.Y, "End moves the text cursor, not the results");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlEnd_WithNoResults_IsLeftToTheTextBox()
    {
        var dialog = CreateDialog(new FileSearchService());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true));

        Assert.AreEqual(0, (int)dialog.ResultListBoxForTest.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALargeFilePreset_OpensWithTheSizeAndAnEmptyName()
    {
        var dialog = CreateDialog(new FileSearchService());
        dialog.Preset(largerThanMegabytes: 100, unchangedForDays: null);

        var root = await ShowAsync(dialog);

        AssertScreenContains(Render(root), "Larger MB");
        AssertScreenContains(Render(root), "100");
    }
}
