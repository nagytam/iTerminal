using iConsole.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>The terminal capability is process-wide, so these run one at a time.</summary>
[TestClass]
[DoNotParallelize]
public class AboutDialogSixelTest
{
    private TerminalGraphics? _saved;

    [TestInitialize]
    public void Save() => _saved = TerminalGraphics.Current;

    [TestCleanup]
    public void Restore() => TerminalGraphics.Current = _saved!;

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnASixelTerminal_TheEmblemIsOfferedAboveTheText()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var overlay = new iConsole.Graphics.SixelOverlay();
        var dialog = new AboutDialog(TestTheme(), null, overlay);
        await ShowAsync(dialog);

        Render(dialog);

        Assert.IsNotNull(overlay.Current);
        Assert.AreEqual(7, overlay.Current.Rows);
        Assert.AreEqual(14, overlay.Current.Columns, "7 rows of 20 pixels, square, in cells of 10 pixels");
        Assert.AreEqual(23, (int)dialog.ViewSize.Height, "the text makes room for the emblem, as on the splash");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingAbout_WithdrawsTheEmblem()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var overlay = new iConsole.Graphics.SixelOverlay();
        var dialog = new AboutDialog(TestTheme(), null, overlay);
        await ShowAsync(dialog);
        Render(dialog);

        dialog.Close();

        Assert.IsNull(overlay.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutSixel_AboutIsTextOnly()
    {
        TerminalGraphics.Current = new TerminalGraphics(false);
        var overlay = new iConsole.Graphics.SixelOverlay();
        var dialog = new AboutDialog(TestTheme(), null, overlay);
        await ShowAsync(dialog);

        Render(dialog);

        Assert.IsNull(overlay.Current);
        Assert.AreEqual(17, (int)dialog.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSplashTextIsOnTheAboutBox()
    {
        TerminalGraphics.Current = new TerminalGraphics(false);
        var dialog = new AboutDialog(TestTheme());
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Swiss Army Terminal Knife");
        AssertScreenContains(screen, "Copyright (C) Tamas Nagy");
    }
}
