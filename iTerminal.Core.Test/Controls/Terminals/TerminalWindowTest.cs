using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Controls.Terminals.Test;

/// <summary>The one-row terminal prompt under the file panels.</summary>
[TestClass]
public class TerminalWindowTest
{
    private static (TerminalWindow Window, TerminalModel Model) Create(int width = 40)
    {
        var terminalModel = new TerminalModel(new TaskManager(), Mock.Of<IContentModelService>());
        var window = new TerminalWindow(terminalModel, Mock.Of<IContentModelService>(), new TestThemeService())
        {
            ViewSize = new Size(width, 1),
        };
        return (window, terminalModel);
    }

    private static KeyEvent Key(char c, ConsoleKey key, bool control = false) => new(c, key, false, false, control);

    private static string Render(TerminalWindow window)
    {
        var buffer = new ScreenBuffer((int)window.ViewSize.Width, 1);
        window.Draw(buffer);
        return buffer.Line(0);
    }

    /// <summary>
    /// Regression test: the terminal's default key-handling branch used to append every unmatched
    /// key's KeyChar to the command line unconditionally, including Ctrl+C, so a chord meant for a
    /// global hot key (e.g. QuitCommand's Ctrl+C) was swallowed as literal input instead of being
    /// left unhandled to bubble up.
    /// </summary>
    [TestMethod]
    public async Task HandleKeyEventAsync_CtrlC_IsNotAppendedToCommandLine()
    {
        var (window, terminalModel) = Create();

        var isHandled = await window.HandleKeyEventAsync(Key('\x03', ConsoleKey.C, control: true));

        Assert.IsFalse(isHandled, "Ctrl+C must not be claimed by the terminal, so it can reach a global hot key");
        Assert.AreEqual(string.Empty, terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_PlainLetter_IsAppendedToCommandLine()
    {
        var (window, terminalModel) = Create();

        var isHandled = await window.HandleKeyEventAsync(Key('c', ConsoleKey.C));

        Assert.IsTrue(isHandled);
        Assert.AreEqual("c", terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task BackspaceRemovesTheLastCharacter()
    {
        var (window, terminalModel) = Create();
        terminalModel.Command.Command = "dir";

        await window.HandleKeyEventAsync(Key('\b', ConsoleKey.Backspace));

        Assert.AreEqual("di", terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task EscapeClearsAHalfTypedCommand()
    {
        var (window, terminalModel) = Create();
        terminalModel.Command.Command = "git status";

        var handled = await window.HandleKeyEventAsync(Key('\x1b', ConsoleKey.Escape));

        Assert.IsTrue(handled);
        Assert.AreEqual(string.Empty, terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task EscapeOnAnEmptyLineIsLeftForSomeoneElse()
    {
        var (window, _) = Create();

        Assert.IsFalse(await window.HandleKeyEventAsync(Key('\x1b', ConsoleKey.Escape)));
    }

    [TestMethod]
    public async Task FunctionKeysAreLeftForTheGlobalHotKeys()
    {
        var (window, terminalModel) = Create();

        Assert.IsFalse(await window.HandleKeyEventAsync(Key('\0', ConsoleKey.F5)));
        Assert.AreEqual(string.Empty, terminalModel.Command.Command);
    }

    [TestMethod]
    public async Task TypingIsIgnoredWhileACommandRuns()
    {
        var (window, terminalModel) = Create();
        terminalModel.Command.Command = "ping";
        terminalModel.Command.CommandTask = new TaskCompletionSource().Task;

        var handled = await window.HandleKeyEventAsync(Key('x', ConsoleKey.X));
        await window.HandleKeyEventAsync(Key('\b', ConsoleKey.Backspace));

        Assert.IsTrue(handled, "the key is consumed rather than leaking to another control");
        Assert.AreEqual("ping", terminalModel.Command.Command, "the running command's text is not edited");
    }

    [TestMethod]
    public void DrawsThePromptAndTheCommand()
    {
        var (window, terminalModel) = Create();
        terminalModel.Command.Command = "dir";

        Assert.AreEqual("Terminal>dir".PadRight(40), Render(window));
    }

    [TestMethod]
    public void ALongCommandShowsItsEndNotItsStart()
    {
        // The buffer clipped the line on the right — exactly where the typing happens.
        var (window, terminalModel) = Create(width: 20);
        terminalModel.Command.Command = "dotnet test --filter Something";

        var line = Render(window);

        Assert.IsTrue(line.StartsWith("Terminal>…", StringComparison.Ordinal), line);
        Assert.IsTrue(line.EndsWith("Something", StringComparison.Ordinal), line);
    }

    [TestMethod]
    public void OnInitializeLayoutTakesTheParentsWidthAndOneRow()
    {
        var (window, _) = Create();
        var parent = new Control(new TestThemeService()) { ViewSize = new Size(100, 30) };
        parent.AddControl(window);

        window.OnInitializeLayout();

        Assert.AreEqual(new Size(100, 1), window.ViewSize);
    }
}
