using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.Options;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Controls.Terminals;

/// <summary>
/// The AI prompt bar: one prompt row and, below it, the last answer. What is pinned down is what the
/// user can see of what they type and of what comes back.
/// </summary>
// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class AiPromptBarTest
{
    private static (AiPromptBar Bar, AiChatModel Model, Mock<IAiChatService> Chat) Create(int width = 40, int height = 4, bool available = true)
    {
        var model = new AiChatModel { IsAvailable = available, ModelInfo = "m" };
        var chat = new Mock<IAiChatService>();
        chat.Setup(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>())).Returns(Task.CompletedTask);
        chat.Setup(c => c.SendMessageAsync(It.IsAny<AiChatModel>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var theme = new TestThemeService();
        var dialog = new AiModelDialog(model, Options.Create(new AiOptions()), theme);
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        var bar = new AiPromptBar(model, chat.Object, dialog, dateTime.Object, theme)
        {
            ViewSize = new Size(width, height),
        };
        bar.OnInitializeLayout();
        return (bar, model, chat);
    }

    private static KeyEvent Key(char c, ConsoleKey key) => new(c, key, false, false, false);

    private static ScreenBuffer Render(AiPromptBar bar)
    {
        var buffer = new ScreenBuffer((int)bar.ViewSize.Width, (int)bar.ViewSize.Height);
        bar.Draw(buffer);
        return buffer;
    }

    [TestMethod]
    public async Task TypingBuildsThePrompt()
    {
        var (bar, model, _) = Create();

        await bar.HandleKeyEventAsync(Key('h', ConsoleKey.H));
        await bar.HandleKeyEventAsync(Key('i', ConsoleKey.I));
        await bar.HandleKeyEventAsync(Key('\b', ConsoleKey.Backspace));

        Assert.AreEqual("h", model.Input);
        StringAssert.Contains(Render(bar).Line(0), "[m]> h");
    }

    [TestMethod]
    public async Task EscapeClearsThePromptAndIsNotTypedIntoIt()
    {
        var (bar, model, _) = Create();
        model.Input = "question";

        await bar.HandleKeyEventAsync(Key('\x1b', ConsoleKey.Escape));
        await bar.HandleKeyEventAsync(Key('\x1b', ConsoleKey.Escape));

        Assert.AreEqual(string.Empty, model.Input);
    }

    [TestMethod]
    public async Task ControlCharactersAreNotTypedIntoThePrompt()
    {
        var (bar, model, _) = Create();

        await bar.HandleKeyEventAsync(Key('\t', ConsoleKey.Oem1));

        Assert.AreEqual(string.Empty, model.Input);
    }

    [TestMethod]
    public async Task EnterSendsTheQuestion()
    {
        var (bar, model, chat) = Create();
        model.Input = "what is this?";

        await bar.HandleKeyEventAsync(Key('\r', ConsoleKey.Enter));

        chat.Verify(c => c.SendMessageAsync(model, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EnterOnAnEmptyPromptSendsNothing()
    {
        var (bar, _, chat) = Create();

        await bar.HandleKeyEventAsync(Key('\r', ConsoleKey.Enter));

        chat.Verify(c => c.SendMessageAsync(It.IsAny<AiChatModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task WhileTheModelIsUnavailableKeysAreLeftForOthers()
    {
        var (bar, model, _) = Create(available: false);
        model.ModelInfo = "Ollama not running";

        Assert.IsFalse(await bar.HandleKeyEventAsync(Key('a', ConsoleKey.A)));
        StringAssert.Contains(Render(bar).Line(0), "AI  unavailable: Ollama not running");
    }

    [TestMethod]
    public void ALongPromptShowsWhatIsBeingTyped()
    {
        var (bar, model, _) = Create(width: 30);
        model.Input = "please explain the difference between these two files";

        var line = Render(bar).Line(0);

        Assert.IsTrue(line.TrimEnd().EndsWith("two files", StringComparison.Ordinal), line);
        StringAssert.Contains(line, "…");
    }

    [TestMethod]
    public void ResponsesWrapAtWordsNotMidWord()
    {
        var (bar, model, _) = Create(width: 12, height: 4);
        model.LastResponse = "alpha beta gamma";

        var screen = Render(bar);

        Assert.AreEqual("alpha beta", screen.Line(1).TrimEnd());
        Assert.AreEqual("gamma", screen.Line(2).TrimEnd());
    }

    [TestMethod]
    public void ALongerAnswerSaysHowMuchMoreThereIs()
    {
        var (bar, model, _) = Create(width: 40, height: 3); // two response rows
        model.LastResponse = "1\n2\n3\n4\n5";

        var screen = Render(bar);

        StringAssert.Contains(screen.Line(2), "↓ 3 more (PgDn)");
    }

    [TestMethod]
    public async Task PageDownAndPageUpScrollTheAnswer()
    {
        var (bar, model, _) = Create(width: 40, height: 3); // two response rows, a page is one line
        model.LastResponse = "1\n2\n3\n4\n5";
        Render(bar);

        await bar.HandleKeyEventAsync(Key('\0', ConsoleKey.PageDown));
        await bar.HandleKeyEventAsync(Key('\0', ConsoleKey.PageDown));

        Assert.AreEqual(2, bar.ResponseScroll);
        var screen = Render(bar);
        StringAssert.StartsWith(screen.Line(1), "3");
        StringAssert.Contains(screen.Line(1), "↑ 2 more (PgUp)");

        for (var i = 0; i < 10; i++)
        {
            await bar.HandleKeyEventAsync(Key('\0', ConsoleKey.PageDown));
        }

        Assert.AreEqual(3, bar.ResponseScroll, "stops with the last line on the last row");

        await bar.HandleKeyEventAsync(Key('\0', ConsoleKey.PageUp));

        Assert.AreEqual(2, bar.ResponseScroll);
    }

    [TestMethod]
    public async Task ANewAnswerStartsAtItsTop()
    {
        var (bar, model, _) = Create(width: 40, height: 3);
        model.LastResponse = "1\n2\n3\n4\n5";
        Render(bar);
        await bar.HandleKeyEventAsync(Key('\0', ConsoleKey.PageDown));
        Assert.AreEqual(1, bar.ResponseScroll);

        model.LastResponse = "a\nb\nc\nd";
        Render(bar);

        Assert.AreEqual(0, bar.ResponseScroll);
    }

    [TestMethod]
    public async Task AStreamingAnswerKeepsItsScrollPosition()
    {
        var (bar, model, _) = Create(width: 40, height: 3);
        model.LastResponse = "1\n2\n3\n4";
        Render(bar);
        await bar.HandleKeyEventAsync(Key('\0', ConsoleKey.PageDown));

        model.LastResponse += "\n5";
        Render(bar);

        Assert.AreEqual(1, bar.ResponseScroll);
    }

    [TestMethod]
    public void MoreIndicatorIsOverlaidOnTheRightEnd()
    {
        var line = AiPromptBar.MoreIndicator(new string('x', 30), 30, 4);

        Assert.AreEqual(30, line.Length);
        Assert.IsTrue(line.EndsWith(" ↓ 4 more (PgDn) ", StringComparison.Ordinal), line);
        Assert.AreEqual(" ↑ 2 more", AiPromptBar.MoreIndicator(new string('x', 9), 9, -2));
    }

    /// <summary>
    /// With no model behind it the bar used to keep its five rows, four of them blank, under a
    /// one-line "[server error (401)]".
    /// </summary>
    [TestMethod]
    public void AnUnavailableAssistantNeedsOneRow()
    {
        var (bar, model, _) = Create(available: false);
        model.ModelInfo = "server unreachable";

        Assert.IsTrue(bar.IsCollapsed);
        Assert.AreEqual(1, bar.PreferredHeight(5));
    }

    [TestMethod]
    public void WhileCheckingTheBarKeepsItsHeight()
    {
        var (bar, model, _) = Create(available: false);
        model.ModelInfo = "checking...";

        Assert.IsFalse(bar.IsCollapsed);
        Assert.AreEqual(5, bar.PreferredHeight(5));
    }

    [TestMethod]
    public void AnAvailableAssistantKeepsItsHeight()
    {
        var (bar, _, _) = Create(available: true);

        Assert.AreEqual(5, bar.PreferredHeight(5));
    }

    [TestMethod]
    public void TheParentHearsWhenTheBarCollapsesAndExpands()
    {
        var (bar, model, _) = Create(available: false);
        model.ModelInfo = "checking...";
        var changes = 0;
        bar.PreferredHeightChanged += (_, _) => changes++;

        model.ModelInfo = "server unreachable";
        model.ModelInfo = "server timeout";
        model.IsAvailable = true;

        Assert.AreEqual(2, changes, "collapsed once, expanded once; a new reason is not a new height");
    }

    [TestMethod]
    public void TheCollapsedBarSaysWhatIsWrongAndWhereToLook()
    {
        var (bar, model, _) = Create(width: 100, height: 1, available: false);
        model.ModelInfo = "server error (401)";

        var line = Render(bar).Line(0);

        StringAssert.Contains(line, "AI  unavailable: server error (401)");
        StringAssert.Contains(line, "Ctrl+F2");
    }

    [TestMethod]
    public void ModelInfoIsCtrlF2NotAnAltLetterThePanelWouldTake()
    {
        var command = new iTerminal.Core.Commands.AiModelDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F2, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
    }

    [TestMethod]
    public async Task CtrlVPastesIntoThePrompt_OnOneLine()
    {
        var (bar, model, _) = Create();
        await iConsole.Services.TextClipboard.Current.SetTextAsync("explain" + (char)10 + "this error");
        model.Input = "please ";

        await bar.HandleKeyEventAsync(new KeyEvent('v', ConsoleKey.V, false, false, true));

        Assert.AreEqual("please explain this error", model.Input);
    }

    [TestMethod]
    public async Task PastingWhileTheModelIsUnavailableDoesNothing()
    {
        var (bar, model, _) = Create(available: false);
        await iConsole.Services.TextClipboard.Current.SetTextAsync("text");

        await bar.HandleKeyEventAsync(new KeyEvent('v', ConsoleKey.V, false, false, true));

        Assert.AreEqual(string.Empty, model.Input);
    }

    [TestMethod]
    public async Task CtrlWDeletesTheLastWordOfThePrompt_AndCtrlUClearsIt()
    {
        var (bar, model, _) = Create();
        model.Input = "explain this error";

        await bar.HandleKeyEventAsync(new KeyEvent('w', ConsoleKey.W, false, false, true));
        Assert.AreEqual("explain this ", model.Input);

        await bar.HandleKeyEventAsync(new KeyEvent('u', ConsoleKey.U, false, false, true));
        Assert.AreEqual(string.Empty, model.Input);
    }
}
