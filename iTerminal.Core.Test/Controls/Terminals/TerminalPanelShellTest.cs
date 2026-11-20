using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Model;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Controls.Terminals.Test;

/// <summary>Esc while a command runs in the shell session ends the session; with no session it keeps its old meaning.</summary>
[TestClass]
public class TerminalPanelShellTest
{
    private static (TerminalPanel Panel, TerminalModel Model) Create(FakeShellSessions sessions)
    {
        var model = new TerminalModel(new TaskManager(), Mock.Of<IContentModelService>(), sessions);
        var panel = new TerminalPanel(model, Mock.Of<IContentModelService>(), new TestThemeService())
        {
            ViewSize = new Size(40, 5),
        };
        return (panel, model);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_WhileASessionCommandRuns_InterruptsIt()
    {
        var sessions = new FakeShellSessions { IsBusy = true };
        var (panel, model) = Create(sessions);
        model.Command.CommandTask = Task.Delay(TimeSpan.FromSeconds(30));

        var isHandled = await panel.HandleKeyEventAsync(new KeyEvent('\x1b', ConsoleKey.Escape, false, false, false));

        Assert.IsTrue(isHandled);
        Assert.AreEqual(1, sessions.InterruptCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_WhileANonSessionCommandRuns_DoesNotInterrupt()
    {
        var sessions = new FakeShellSessions { IsBusy = false };
        var (panel, model) = Create(sessions);
        model.Command.CommandTask = Task.Delay(TimeSpan.FromSeconds(30));

        await panel.HandleKeyEventAsync(new KeyEvent('\x1b', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(0, sessions.InterruptCount);
    }
}
