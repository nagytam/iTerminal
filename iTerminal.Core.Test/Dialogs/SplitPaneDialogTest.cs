using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The chrome every split-pane dialog shares: section bands and the status-and-keys footer.</summary>
[TestClass]
public class SplitPaneDialogTest
{
    private sealed class Probe() : SplitPaneDialog("Probe", TestTheme())
    {
        public void Footer(IBuffer buffer, int width, string? status) =>
            DrawStatusAndHints(buffer, 0, 0, width, status, Color.Red, "a:add  Esc:close", ThemeService.CurrentColorScheme);

        public void Band(IBuffer buffer, int width, bool focused) =>
            DrawSectionHeader(buffer, 0, 0, width, "Title", focused, ThemeService.CurrentColorScheme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAStatusTheFooterIsTheKeys()
    {
        var buffer = new ScreenBuffer(40, 1);

        new Probe().Footer(buffer, 40, null);

        Assert.AreEqual("a:add  Esc:close", buffer.Line(0).TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStatusComesFirstInItsColourAndTheKeysFollow()
    {
        var buffer = new ScreenBuffer(40, 1);

        new Probe().Footer(buffer, 40, "Saved.");

        Assert.AreEqual("Saved.  │ a:add  Esc:close", buffer.Line(0).TrimEnd());
        Assert.AreEqual(Color.Red, buffer.ForegroundAt(0, 0));
        Assert.AreNotEqual(Color.Red, buffer.ForegroundAt(10, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStatusTooLongForBothKeepsTheStatus()
    {
        var buffer = new ScreenBuffer(10, 1);

        new Probe().Footer(buffer, 10, "A long failure message");

        Assert.AreEqual("A long fai", buffer.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFooterFillsItsRowSoNothingOlderShowsThrough()
    {
        var buffer = new ScreenBuffer(40, 1);

        new Probe().Footer(buffer, 40, "x");

        Assert.IsTrue(Enumerable.Range(0, 40).All(x => buffer.IsWritten(x, 0)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoWidthDrawsNothing()
    {
        var buffer = new ScreenBuffer(10, 1);

        new Probe().Footer(buffer, 0, "x");

        Assert.IsFalse(buffer.IsWritten(0, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFocusedSectionBandUsesTheSelectionColours()
    {
        var scheme = TestTheme().CurrentColorScheme;
        var focused = new ScreenBuffer(20, 1);
        var unfocused = new ScreenBuffer(20, 1);

        new Probe().Band(focused, 20, focused: true);
        new Probe().Band(unfocused, 20, focused: false);

        Assert.AreEqual(scheme.SelectedFocusedBackground, focused.BackgroundAt(0, 0));
        Assert.AreEqual(scheme.ButtonBackground, unfocused.BackgroundAt(0, 0));
        Assert.AreEqual(" Title", focused.Line(0).TrimEnd());
    }
}
