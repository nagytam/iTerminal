using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Controls.Terminals.Test;

/// <summary>The full-height terminal panel: scrollable history above, the prompt on the last row.</summary>
[TestClass]
public class TerminalPanelTest
{
    private static (TerminalPanel Panel, TerminalModel Model) Create(int width = 40, int height = 5)
    {
        var terminalModel = new TerminalModel(new TaskManager(), Mock.Of<IContentModelService>());
        var panel = new TerminalPanel(terminalModel, Mock.Of<IContentModelService>(), new TestThemeService())
        {
            ViewSize = new Size(width, height),
        };
        return (panel, terminalModel);
    }

    private static KeyEvent Key(char c, ConsoleKey key, bool control = false) => new(c, key, false, false, control);

    private static ScreenBuffer Render(TerminalPanel panel)
    {
        var buffer = new ScreenBuffer((int)panel.ViewSize.Width, (int)panel.ViewSize.Height);
        panel.Draw(buffer);
        return buffer;
    }

    private static void AddHistory(TerminalModel model, string command, string output, string? error = null) =>
        model.History.Add(new TerminalCommand { Command = command, Output = output, Error = error });

    /// <summary>
    /// Regression test: same bug as <see cref="TerminalWindowTest.HandleKeyEventAsync_CtrlC_IsNotAppendedToCommandLine"/> —
    /// the panel's default key-handling branch appended every unmatched KeyChar unconditionally,
    /// swallowing Ctrl+C instead of leaving it unhandled to bubble up to a global hot key.
    /// </summary>
    [TestMethod]
    public async Task HandleKeyEventAsync_CtrlC_IsNotAppendedToCommandLine()
    {
        var (panel, terminalModel) = Create();

        var isHandled = await panel.HandleKeyEventAsync(Key('\x03', ConsoleKey.C, control: true));

        Assert.IsFalse(isHandled, "Ctrl+C must not be claimed by the terminal, so it can reach a global hot key");
        Assert.AreEqual(string.Empty, terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_PlainLetter_IsAppendedToCommandLine()
    {
        var (panel, terminalModel) = Create();

        var isHandled = await panel.HandleKeyEventAsync(Key('c', ConsoleKey.C));

        Assert.IsTrue(isHandled);
        Assert.AreEqual("c", terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task EscapeClearsAHalfTypedCommand()
    {
        var (panel, terminalModel) = Create();
        terminalModel.Command.Command = "ls -la";

        await panel.HandleKeyEventAsync(Key('\x1b', ConsoleKey.Escape));

        Assert.AreEqual(string.Empty, terminalModel.Command.Command);
    }

    [TestMethod]
    public void DrawsTheHistoryAboveThePrompt()
    {
        var (panel, terminalModel) = Create();
        AddHistory(terminalModel, "echo hi", "hi", "warning");
        terminalModel.Command.Command = "next";

        var screen = Render(panel);

        CollectionAssert.AreEqual(
            new[] { "> echo hi", "hi", "ERR: warning", "", "Terminal>next" },
            screen.Lines);
        Assert.AreEqual(Color.Red, screen.ForegroundAt(0, 2), "errors are red");
        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(0, 0), "commands are cyan");
    }

    [TestMethod]
    public void OutputIsSplitOnEitherLineEnding()
    {
        // Split on Environment.NewLine, a tool writing LF on Windows came out as one line with the
        // breaks drawn as garbage.
        var (panel, terminalModel) = Create(height: 6);
        AddHistory(terminalModel, "tool", "one\ntwo\r\nthree");

        var screen = Render(panel);

        CollectionAssert.AreEqual(new[] { "> tool", "one", "two", "three", "", "Terminal>" }, screen.Lines);
    }

    [TestMethod]
    public async Task UpScrollsBackAndStopsAtTheFirstLine()
    {
        var (panel, terminalModel) = Create(height: 3); // two output rows
        AddHistory(terminalModel, "a", "1\n2\n3"); // four lines

        for (var i = 0; i < 10; i++)
        {
            await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.UpArrow));
        }

        // The old limit let the offset run on to one short of the line count, scrolling the output
        // off the top and leaving blank rows.
        Assert.AreEqual(2, panel.ScrollOffset);
        CollectionAssert.AreEqual(new[] { "> a", "1", "Terminal>" }, Render(panel).Lines);
    }

    [TestMethod]
    public async Task DownScrollsForwardAndStopsAtTheLatestOutput()
    {
        var (panel, terminalModel) = Create(height: 3);
        AddHistory(terminalModel, "a", "1\n2\n3");
        await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.UpArrow));

        await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.DownArrow));
        await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.DownArrow));

        Assert.AreEqual(0, panel.ScrollOffset);
    }

    [TestMethod]
    public async Task PageUpAndPageDownScrollByAPage()
    {
        var (panel, terminalModel) = Create(height: 5); // four output rows, a page is three lines
        AddHistory(terminalModel, "a", string.Join("\n", Enumerable.Range(1, 20)));

        await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.PageUp));
        Assert.AreEqual(3, panel.ScrollOffset);

        await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.PageUp));
        Assert.AreEqual(6, panel.ScrollOffset);

        await panel.HandleKeyEventAsync(Key('\0', ConsoleKey.PageDown));
        Assert.AreEqual(3, panel.ScrollOffset);
    }

    [TestMethod]
    public void NewOutputReturnsToTheBottom()
    {
        var (panel, terminalModel) = Create(height: 3);
        AddHistory(terminalModel, "a", "1\n2\n3");
        panel.HandleKeyEventAsync(Key('\0', ConsoleKey.UpArrow)).GetAwaiter().GetResult();
        Assert.AreEqual(1, panel.ScrollOffset);

        terminalModel.Command = new TerminalCommand();

        Assert.AreEqual(0, panel.ScrollOffset);
    }

    [TestMethod]
    public void ALongCommandShowsItsEnd()
    {
        var (panel, terminalModel) = Create(width: 20, height: 2);
        terminalModel.Command.Command = "dotnet test --filter Something";

        var prompt = Render(panel).Line(1);

        Assert.IsTrue(prompt.StartsWith("Terminal>…", StringComparison.Ordinal), prompt);
        Assert.IsTrue(prompt.EndsWith("Something", StringComparison.Ordinal), prompt);
    }

    [TestMethod]
    public async Task TypingIsIgnoredWhileACommandRuns()
    {
        var (panel, terminalModel) = Create();
        terminalModel.Command.Command = "ping";
        terminalModel.Command.CommandTask = new TaskCompletionSource().Task;

        await panel.HandleKeyEventAsync(Key('x', ConsoleKey.X));

        Assert.AreEqual("ping", terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task CtrlW_DeletesTheLastWordOfTheCommand_AndCtrlU_ClearsIt()
    {
        var (panel, model) = Create();
        model.Command.Command = "git commit -m";

        Assert.IsTrue(await panel.HandleKeyEventAsync(Key('w', ConsoleKey.W, control: true)));
        Assert.AreEqual("git commit ", model.Command.Command);
        await panel.HandleKeyEventAsync(Key('\b', ConsoleKey.Backspace, control: true));
        Assert.AreEqual("git ", model.Command.Command);

        Assert.IsTrue(await panel.HandleKeyEventAsync(Key('u', ConsoleKey.U, control: true)));
        Assert.AreEqual(string.Empty, model.Command.Command);
    }

    [TestMethod]
    public async Task CtrlWAndCtrlU_OnAnEmptyCommand_AreLeftForOthers()
    {
        var (panel, model) = Create();

        Assert.IsFalse(await panel.HandleKeyEventAsync(Key('w', ConsoleKey.W, control: true)));
        Assert.IsFalse(await panel.HandleKeyEventAsync(Key('u', ConsoleKey.U, control: true)));
        Assert.AreEqual(string.Empty, model.Command.Command);
    }
}
