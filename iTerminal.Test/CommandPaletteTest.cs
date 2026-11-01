namespace iTerminal.Test;

/// <summary>Shift+F1: find a command by name and run it — global, panel and menu-only commands alike.</summary>
[TestClass]
public class CommandPaletteTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task OpenAsync()
    {
        await _app.PressAsync("Shift+F1");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.CommandPaletteDialog>();
        await _app.WaitForTextAsync("Enter:run");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TypingNarrowsTheListAndShowsTheKey()
    {
        await OpenAsync();

        await _app.TypeAsync("calc");

        var screen = await _app.WaitForTextAsync("Calculator");
        Assert.IsTrue(screen.Contains("Ctrl+A"), "the key is shown beside the command" + Environment.NewLine + screen.Dump());
        Assert.IsFalse(screen.Contains("Unit converter"), screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EnterRunsAGlobalCommand()
    {
        await OpenAsync();
        await _app.TypeAsync("calculator");

        await _app.PressAsync("Enter");

        await _app.Driver.WaitForModalAsync<Core.Dialogs.Calculator.CalculatorDialog>();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task APanelCommandRunsOnThePanelThePaletteWasOpenedOver()
    {
        await _app.SelectEntryAsync("readme.txt");
        await OpenAsync();
        await _app.TypeAsync("copy");
        await _app.WaitForTextAsync("F5");

        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("Copy file");
        await _app.WaitForTextAsync("readme.txt");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ACommandWithNoKeyIsReachableToo()
    {
        await OpenAsync();
        await _app.TypeAsync("mark all");
        await _app.WaitForTextAsync("Mark all");

        await _app.PressAsync("Enter");

        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextAsync("4 marked");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EscClosesItAndTheLastCommandRunComesFirstNextTime()
    {
        await OpenAsync();
        await _app.TypeAsync("unit");
        await _app.PressAsync("Enter");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.UnitConverter.UnitConverterDialog>();
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        await OpenAsync();
        var palette = (Core.Dialogs.CommandPaletteDialog)_app.Driver.FocusedModal!;
        Assert.AreEqual("Unit Converter", palette.Matches[0].Title, "the command run last leads the empty list");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }
}
