using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Dialogs;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class FolderSelectorDialogCommandTest
{
    private static FolderSelectorDialogCommand Create(
        FolderSelectorTestHarness harness,
        HelpDialog? helpDialog = null)
    {
        return new FolderSelectorDialogCommand(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.DriveInfoModel,
            harness.FolderIndexModel,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.FrequentExecutionGuardService,
            harness.ThemeService,
            helpDialog);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsCtrlF1()
    {
        // Key's arguments run (consoleKey, shift, alt, control) — not the order the properties are
        // declared in — so this is worth asserting rather than reading.
        var command = Create(new FolderSelectorTestHarness());

        Assert.AreEqual(ConsoleKey.F1, command.Key?.ConsoleKey);
        Assert.IsTrue(command.Key!.Control);
        Assert.IsFalse(command.Key.Alt);
        Assert.IsFalse(command.Key.Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TitleAndDescription_AreWrittenForHelp()
    {
        var command = Create(new FolderSelectorTestHarness());

        Assert.AreEqual("Select drive or folder", command.Title);
        Assert.AreNotEqual(command.Title, command.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShowsTheSelectorOnTheControlItIsAttachedTo()
    {
        var harness = new FolderSelectorTestHarness();
        var host = new Control(harness.ThemeService) { ViewSize = new Size(120, 40) };
        var command = Create(harness);
        host.AddCommand(command);

        await command.ExecuteAsync(null);

        Assert.AreEqual(1, host.Controls.AsSnapshot().OfType<FolderSelectorDialog>().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_PassesTheHelpDialogOnSoF1WorksInsideTheSelector()
    {
        var harness = new FolderSelectorTestHarness();
        var host = new Control(harness.ThemeService) { ViewSize = new Size(120, 40) };
        var command = Create(harness, new HelpDialog(harness.ThemeService));
        host.AddCommand(command);

        await command.ExecuteAsync(null);

        var dialog = host.Controls.AsSnapshot().OfType<FolderSelectorDialog>().Single();
        Assert.IsTrue(dialog.Commands.Any(c => c is iConsole.Commands.HelpDialogCommand));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithNoControl_DoesNothing()
    {
        var command = Create(new FolderSelectorTestHarness());

        await command.ExecuteAsync(null);
    }
}
