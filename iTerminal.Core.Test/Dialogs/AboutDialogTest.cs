using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class AboutDialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowsWhatThisIsAndWhichBuild()
    {
        // It used to be an empty frame with nothing in it but its title.
        var dialog = new AboutDialog(TestTheme());
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "iTerminal");
        AssertScreenContains(screen, "Version   " + AboutDialog.Version);
        AssertScreenContains(screen, "Runtime   .NET");
        AssertScreenContains(screen, "Esc:close");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheVersionHasNoCommitHash()
    {
        Assert.IsFalse(AboutDialog.Version.Contains('+'), AboutDialog.Version);
        Assert.IsFalse(string.IsNullOrWhiteSpace(AboutDialog.Version));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDialogIsTallEnoughForEveryLine()
    {
        var dialog = new AboutDialog(TestTheme());
        await ShowAsync(dialog);

        Assert.IsTrue(dialog.ViewSize.Height >= AboutDialog.Lines.Count + 3);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = new AboutDialog(TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
