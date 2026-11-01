using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iConsole.Test.Commands;

/// <summary>Space on a toggle flips it, but not when it is read-only; Escape in a search box empties the query and says so.</summary>
[TestClass]
public class ToggleAndClearSearchCommandTest
{
    private static TestThemeService Theme() => new();

    [Timeout(60_000)]
    [TestMethod]
    public async Task ToggleCommand_FlipsTheFocusedToggle()
    {
        var toggle = new ToggleButton(Theme());
        var command = new ToggleCommand(new Key(ConsoleKey.Spacebar, false, false, false)) { Control = toggle };

        Assert.IsTrue(command.CanExecute());
        await command.ExecuteAsync(null);

        Assert.IsTrue(toggle.IsOn);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleCommand_DoesNotRunOnAReadOnlyToggle()
    {
        var toggle = new ToggleButton(Theme()) { IsReadOnly = true };
        var command = new ToggleCommand(new Key(ConsoleKey.Spacebar, false, false, false)) { Control = toggle };

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClearSearchCommand_EmptiesTheQueryAndSaysSo()
    {
        var box = new SearchBox(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme()) { Text = "invoices" };
        var cleared = false;
        box.Cleared += (_, _) => cleared = true;
        var command = new ClearSearchCommand { Control = box };

        await command.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, box.Text);
        Assert.IsTrue(cleared);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ClearSearchCommand_IsEscape()
    {
        Assert.AreEqual(ConsoleKey.Escape, new ClearSearchCommand().Key!.ConsoleKey);
    }
}
