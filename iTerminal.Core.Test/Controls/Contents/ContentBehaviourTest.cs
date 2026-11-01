using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// The main area: two file panels over a bottom bar (the AI prompt or the terminal), or the terminal
/// full screen. What is pinned down is what is on screen, and where the cursor is, after each switch.
/// </summary>
[TestClass]
public class ContentBehaviourTest
{
    private sealed record Setup(Content Content, FilePanel Left, FilePanel Right, AiPromptBar AiBar, TerminalPanel Terminal);

    private const int BarHeight = 5;

    private static Setup Create(int width = 120, int height = 40)
    {
        var theme = TestTheme();
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var contentModelService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(taskManager, contentModelService.Object);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var factory = new FilePanelFactory(
            taskManager, contentModelService.Object, terminalModel, Mock.Of<IMemoryCacheService>(), settings.Object, Mock.Of<ILogger>(), theme);

        var aiModel = new AiChatModel();
        var aiBar = new AiPromptBar(aiModel, Mock.Of<IAiChatService>(), new iTerminal.Core.Dialogs.AiModelDialog(aiModel, Options.Create(new AiOptions()), theme), Mock.Of<IDateTimeProviderService>(), theme);
        var terminal = new TerminalPanel(terminalModel, contentModelService.Object, theme);
        var content = new Content(factory, aiBar, terminal, contentModelService.Object, theme, Options.Create(new AiOptions { BarHeight = BarHeight }))
        {
            ViewSize = new Size(width, height),
        };
        content.OnInitializeLayout();

        var panels = content.Controls.AsSnapshot().OfType<FilePanel>().ToList();
        return new Setup(content, panels[0], panels[1], aiBar, terminal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AtAnOddWidthThePanelsStillCoverEveryColumn()
    {
        // Two halves of 121 left column 120 to nobody, and nothing repainted it.
        var s = Create(width: 121);

        Assert.AreEqual(60, (int)s.Left.ViewSize.Width);
        Assert.AreEqual(61, (int)s.Right.ViewSize.Width);
        Assert.AreEqual(60, (int)s.Right.Position.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePanelsSitAboveTheBottomBar()
    {
        var s = Create(height: 40);

        Assert.AreEqual(40 - BarHeight, (int)s.Left.ViewSize.Height);
        Assert.AreEqual(40 - BarHeight, (int)s.AiBar.Position.Y);
        Assert.IsTrue(s.AiBar.IsVisible);
        Assert.IsFalse(s.Terminal.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFullScreenTerminalHidesEverythingElseAndTakesTheWholeArea()
    {
        var s = Create();

        s.Content.ToggleTerminalPanel();

        Assert.IsTrue(s.Terminal.IsVisible);
        Assert.IsTrue(s.Terminal.IsFocused);
        Assert.IsFalse(s.Left.IsVisible || s.Right.IsVisible || s.AiBar.IsVisible);
        Assert.AreEqual(s.Content.ViewSize, s.Terminal.ViewSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ClosingTheTerminalReturnsToThePanelThatHadTheCursor()
    {
        // It always went back to the left panel.
        var s = Create();
        s.Right.SetFocus();

        s.Content.ToggleTerminalPanel();
        s.Content.ToggleTerminalPanel();

        Assert.IsTrue(s.Right.IsFocused);
        Assert.IsTrue(s.Left.IsVisible && s.Right.IsVisible && s.AiBar.IsVisible);
        Assert.IsFalse(s.Terminal.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CyclingTheBottomBarSwapsTheAiPromptForTheTerminalAndBack()
    {
        var s = Create();

        s.Content.CycleBottomBarMode();

        Assert.IsTrue(s.Terminal.IsVisible && s.Terminal.IsFocused);
        Assert.IsFalse(s.AiBar.IsVisible);
        Assert.AreEqual(new Size(120, BarHeight), s.Terminal.ViewSize, "a bar, not the whole screen");
        Assert.AreEqual(40 - BarHeight, (int)s.Terminal.Position.Y);

        s.Content.CycleBottomBarMode();

        Assert.IsTrue(s.AiBar.IsVisible && s.AiBar.IsFocused);
        Assert.IsFalse(s.Terminal.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CyclingIsIgnoredWhileTheTerminalIsFullScreen()
    {
        var s = Create();
        s.Content.ToggleTerminalPanel();

        s.Content.CycleBottomBarMode();

        Assert.IsTrue(s.Terminal.IsVisible);
        Assert.IsFalse(s.Left.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheShowMethodsPutTheScreenInEachState()
    {
        var s = Create();

        s.Content.ShowFullTerminal();
        Assert.IsTrue(s.Terminal.IsVisible && !s.Left.IsVisible);

        s.Content.ShowTerminalBar();
        Assert.IsTrue(s.Terminal.IsVisible && s.Left.IsVisible && !s.AiBar.IsVisible);

        s.Content.ShowAiBar();
        Assert.IsTrue(s.AiBar.IsVisible && s.AiBar.IsFocused && !s.Terminal.IsVisible);

        s.Content.ShowFilePanels();
        Assert.IsTrue(s.Left.IsVisible && s.Left.IsFocused && s.AiBar.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFocusMethodsMoveTheCursor()
    {
        var s = Create();

        s.Content.FocusRightPanel();
        Assert.IsTrue(s.Right.IsFocused);

        s.Content.FocusAiBar();
        Assert.IsTrue(s.AiBar.IsFocused);

        s.Content.FocusLeftPanel();
        Assert.IsTrue(s.Left.IsFocused);

        s.Content.ShowTerminalBar();
        s.Content.FocusTerminalBar();
        Assert.IsTrue(s.Terminal.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlShiftOTogglesTheCommandLog()
    {
        // Ctrl+O is the interactive shell now; the full-screen command log moved to Ctrl+Shift+O.
        // (It was once bound to Alt+O — which the focused file panel takes for its type-to-search,
        // so the key never reached the command.)
        var s = Create();

        var handled = await s.Content.HandleKeyEventAsync(Key(ConsoleKey.O, '\x0f', shift: true, control: true));

        Assert.IsTrue(handled);
        Assert.IsTrue(s.Terminal.IsVisible && !s.Left.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TabCyclesThroughThePanelsAndTheBottomBarAndShiftTabGoesBack()
    {
        // Driven through the browsers' own Tab handlers: the key binding itself needs a loaded
        // content model, which this harness does not build.
        var s = Create();
        s.Content.FocusLeftPanel();
        ContentBrowser Browser(FilePanel panel) => panel.Controls.AsSnapshot().OfType<ContentBrowser>().Single();

        Browser(s.Left).OnHandleTabForward();
        Assert.IsTrue(s.Right.IsFocused, "left to right");

        Browser(s.Right).OnHandleTabForward();
        Assert.IsTrue(s.AiBar.IsFocused, "right to the AI prompt");

        s.AiBar.OnHandleTabBackward();
        Assert.IsTrue(s.Right.IsFocused, "and back");

        Browser(s.Left).OnHandleTabBackward();
        Assert.IsTrue(s.AiBar.IsFocused, "back from the left panel wraps round to the bottom bar");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FromTheTerminalBarTabGoesBackToThePanels()
    {
        var s = Create();
        s.Content.ShowTerminalBar();
        Assert.IsTrue(s.Terminal.IsFocused);

        s.Terminal.OnHandleTabForward();
        Assert.IsTrue(s.Left.IsFocused, "on round to the left panel");

        s.Content.ShowTerminalBar();
        s.Terminal.OnHandleTabBackward();
        Assert.IsTrue(s.Right.IsFocused, "back to the right panel");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InTheFullScreenTerminalTabStaysInTheTerminal()
    {
        var s = Create();
        s.Content.ShowFullTerminal();

        s.Terminal.OnHandleTabForward();

        Assert.IsTrue(s.Terminal.IsFocused, "nothing else is on screen to go to");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResizingLaysTheAreaOutAgain()
    {
        var s = Create();

        s.Content.OnWindowSizeChanged(this, new Size(80, 20));

        Assert.AreEqual(40, (int)s.Left.ViewSize.Width);
        Assert.AreEqual(20 - BarHeight, (int)s.Left.ViewSize.Height);
    }
}
