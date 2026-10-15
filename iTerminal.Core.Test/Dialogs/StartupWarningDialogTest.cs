using iConsole.Data;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class StartupWarningDialogTest
{
    private const string LongWarning =
        "Another iTerminal instance holds the database; settings changed in this window will not be saved until it closes.";

    [TestMethod]
    public async Task ALongWarningIsWrappedNotCutOff()
    {
        // The second half of a warning — usually the part saying what to do — used to be cut at the border.
        var dialog = new StartupWarningDialog([LongWarning], TestTheme());
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Another iTerminal instance holds the database;");
        AssertScreenContains(screen, "until it closes.");
    }

    [TestMethod]
    public async Task EachWarningStartsOnItsOwnLine()
    {
        var dialog = new StartupWarningDialog(["First problem.", "Second problem."], TestTheme());
        await ShowAsync(dialog);

        var screen = Render(dialog);
        var first = screen.Find("First problem.");
        var second = screen.Find("Second problem.");

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first!.Y + 1, second!.Y);
    }

    [TestMethod]
    public async Task SaysHowToDismissIt()
    {
        var dialog = new StartupWarningDialog(["x"], TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), StartupWarningDialog.DismissHint);
    }

    [TestMethod]
    public async Task OnANarrowScreenTheDialogFitsInsteadOfGoingOffTheLeftEdge()
    {
        // Centred by hand as (parent - 80) / 2, a terminal narrower than 80 columns produced a
        // negative position.
        var screen = new Size(50, 20);
        var dialog = new StartupWarningDialog([LongWarning], TestTheme());
        await ShowAsync(dialog, screen);

        Assert.IsTrue(dialog.ViewSize.Width <= 50, $"width {dialog.ViewSize.Width}");
        Assert.IsTrue(dialog.Position.X + dialog.ViewSize.Width <= 50);
        AssertScreenContains(Render(dialog, screen), "until it closes.");
    }

    [TestMethod]
    public async Task TheHeightFollowsTheWrappedText()
    {
        var shortDialog = new StartupWarningDialog(["short"], TestTheme());
        var longDialog = new StartupWarningDialog([LongWarning, LongWarning], TestTheme());
        await ShowAsync(shortDialog);
        await ShowAsync(longDialog);

        Assert.IsTrue(longDialog.ViewSize.Height > shortDialog.ViewSize.Height);
    }

    [TestMethod]
    public async Task TooManyWarningsForTheScreenEndWithAnEllipsis()
    {
        var screen = new Size(80, 8);
        var warnings = Enumerable.Range(1, 10).Select(i => $"Warning number {i}.").ToList();
        var dialog = new StartupWarningDialog(warnings, TestTheme());
        await ShowAsync(dialog, screen);

        var lines = dialog.VisibleLines(78).ToList();

        Assert.IsTrue(lines.Count < warnings.Count);
        Assert.IsTrue(lines[^1].Line.EndsWith("…", StringComparison.Ordinal), lines[^1].Line);
    }

    [TestMethod]
    public async Task EnterClosesIt()
    {
        var dialog = new StartupWarningDialog(["x"], TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = new StartupWarningDialog(["x"], TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
