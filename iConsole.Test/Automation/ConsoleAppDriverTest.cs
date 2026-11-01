using iConsole.Dialogs;
using iConsole.Test.Core.Automation;

namespace iConsole.Test.Automation;

/// <summary>
/// The console-application driver against a real (small) iConsole application: generic host,
/// desktop, input job and both render jobs, with only the terminal itself virtual.
/// </summary>
[TestClass]
[DoNotParallelize] // A whole application per test, with render threads at the highest thread priority.
public class ConsoleAppDriverTest
{
    private ConsoleAppDriver _app = null!;

    [TestInitialize]
    public async Task StartAsync()
    {
        _app = await ConsoleAppDriver.StartAsync<SampleScreen>(options: new ConsoleAppDriverOptions { Width = 80, Height = 24 });
    }

    [TestCleanup]
    public async Task StopAsync()
    {
        await _app.DisposeAsync();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFirstFrameIsOnScreenWhenStartReturns()
    {
        var screen = _app.Screen;

        Assert.AreEqual(80, screen.Width);
        Assert.AreEqual(24, screen.Height);
        Assert.IsFalse(screen.IsBlank);
        await _app.WaitForTextAsync(SampleScreen.Hint);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHintIsOnTheLastRow()
    {
        var screen = await _app.WaitForTextAsync(SampleScreen.Hint);

        Assert.AreEqual(23, screen.RowOf(SampleScreen.Hint));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypedTextReachesTheFocusedTextBox()
    {
        await _app.TypeAsync("Ada");

        var screen = await _app.WaitForTextAsync("Ada");
        Assert.AreEqual(2, screen.RowOf("Ada"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BackspaceEditsWhatWasTyped()
    {
        await _app.TypeAsync("Adam");
        await _app.PressAsync("Backspace");

        await _app.WaitUntilAsync(screen => screen.Line(2).Contains("Ada") && !screen.Line(2).Contains("Adam"),
            "the last letter to be erased");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AKeyBoundToACommandOpensADialogOnTop()
    {
        await _app.TypeAsync("Ada");

        Assert.IsTrue(await _app.PressAsync("F2"));

        await _app.WaitForTextAsync("Hello, Ada!");
        await _app.WaitForModalAsync<GreetingDialog>();
        Assert.IsTrue(_app.CommandLogger.Records.Any(r => r.CommandName == "Greet" && r.Executed),
            "the command log records what ran");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesTheDialogAndKeysGoBackToTheScreen()
    {
        await _app.TypeAsync("Ada");
        await _app.PressAsync("F2");
        await _app.WaitForTextAsync("Hello, Ada!");

        await _app.PressAsync("Esc");

        await _app.WaitForTextGoneAsync("Hello, Ada!");
        Assert.IsNotInstanceOfType<Dialog>(_app.FocusedModal);
        await _app.TypeAsync("!");
        await _app.WaitForTextAsync("Ada!");
    }

    /// <summary>
    /// A closed dialog must leave no picture of itself behind, even while another control still
    /// draws on the layer it used. Layers were never cleared, so it stayed on screen.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task AClosedDialogLeavesNoGhostOnASharedLayer()
    {
        await _app.WaitForTextAsync(SampleScreen.Badge);
        await _app.PressAsync("F2");
        await _app.WaitForTextAsync("Greeting");

        await _app.PressAsync("Esc");

        var screen = await _app.WaitForTextGoneAsync("Greeting");
        Assert.IsFalse(screen.Contains("Hello, !"), screen.Dump());
        Assert.IsFalse(screen.Contains("╔"), "the dialog's frame is gone too:" + Environment.NewLine + screen.Dump());
        Assert.IsTrue(screen.Contains(SampleScreen.Badge), "the control sharing the layer is still drawn");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingAButtonRunsIt()
    {
        await _app.PressAsync("F2");
        await _app.WaitForTextAsync("Hello, !");

        Assert.IsTrue(await _app.ClickAsync("OK"));

        await _app.WaitForTextGoneAsync("Hello, !");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnhandledKeyBeepsAndReportsFalse()
    {
        var beepsBefore = _app.Console.BeepCount;

        Assert.IsFalse(await _app.PressAsync("F12"));

        Assert.AreEqual(beepsBefore + 1, _app.Console.BeepCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AThrowingHandlerFailsThatKeyAndTheKeyboardKeepsWorking()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _app.PressAsync("F3"));

        await _app.TypeAsync("still here");
        await _app.WaitForTextAsync("still here");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ResizingTheTerminalReLaysOutTheScreen()
    {
        var screen = await _app.ResizeAsync(100, 30);

        screen = await _app.WaitUntilAsync(s => s.RowOf(SampleScreen.Hint) == 29, "the hint to move to the new last row");
        Assert.AreEqual(100, screen.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShrinkingTheTerminalLeavesNothingOffScreen()
    {
        await _app.ResizeAsync(60, 12);

        await _app.WaitUntilAsync(s => s.RowOf(SampleScreen.Hint) == 11, "the hint to move up to row 11");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F10QuitsTheApplication()
    {
        await _app.PressAsync("F10");

        await _app.WaitForExitAsync();
        Assert.IsTrue(_app.HasExited);
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() => _app.PressAsync("a"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlCQuitsTheApplication()
    {
        await _app.PressAsync("Ctrl+C");

        await _app.WaitForExitAsync();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AWaitThatNeverHoldsFailsWithTheScreenInTheMessage()
    {
        var failure = await Assert.ThrowsExactlyAsync<AssertFailedException>(
            () => _app.WaitForTextAsync("not on screen", TimeSpan.FromMilliseconds(100)));

        StringAssert.Contains(failure.Message, "'not on screen' to appear");
        StringAssert.Contains(failure.Message, SampleScreen.Hint);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WaitForIdleReturnsOnceTheScreenStopsChanging()
    {
        await _app.TypeAsync("abc");

        var settled = await _app.WaitForIdleAsync();

        StringAssert.Contains(settled.Line(2), "abc");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMouseWheelIsDelivered()
    {
        // Nothing in the sample scrolls; what matters is that the event is routed and answered.
        Assert.IsFalse(await _app.ScrollAsync(5, 5, -1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DisposingTwiceIsHarmless()
    {
        await _app.DisposeAsync();
        await _app.DisposeAsync();
    }
}
