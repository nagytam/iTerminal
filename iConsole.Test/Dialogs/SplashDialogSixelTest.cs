using iConsole.Controls;
using iConsole.Data;
using iConsole.Graphics;
using iConsole.Services;
using iConsole.Test.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Dialogs.Test;

/// <summary>The terminal capability is process-wide, so these run one at a time.</summary>
[TestClass]
[DoNotParallelize]
public class SplashDialogSixelTest
{
    private TerminalGraphics? _saved;

    [TestInitialize]
    public void Save() => _saved = TerminalGraphics.Current;

    [TestCleanup]
    public void Restore() => TerminalGraphics.Current = _saved!;

    private static async Task<SplashDialog> CreateAsync(SixelOverlay? overlay)
    {
        var theme = new TestThemeService();
        var parent = new Control(theme) { ViewSize = new Size(100, 40) };
        var splash = new SplashDialog(theme, overlay) { Parent = parent };
        await splash.OnInitializeAsync();
        splash.OnInitializeLayout();
        return splash;
    }

    [TestMethod]
    public async Task OnASixelTerminal_TheEmblemIsOfferedToTheRenderer()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var overlay = new SixelOverlay();
        var splash = await CreateAsync(overlay);

        splash.Draw(new MockBuffer(new Size(100, 40)));

        Assert.IsNotNull(overlay.Current);
        Assert.AreEqual(7, overlay.Current.Rows);
        Assert.AreEqual(14, overlay.Current.Columns, "7 rows of 20 pixels, square, in cells of 10 pixels");
        Assert.AreEqual(15, (int)splash.ViewSize.Height, "the text makes room for the emblem");
        Assert.IsTrue(overlay.Current.Data.StartsWith("\u001bP", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ClosingTheSplash_WithdrawsTheEmblem()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var overlay = new SixelOverlay();
        var splash = await CreateAsync(overlay);
        splash.Draw(new MockBuffer(new Size(100, 40)));

        splash.Close();

        Assert.IsNull(overlay.Current);
    }

    [TestMethod]
    public async Task WithoutSixel_TheSplashIsTextOnly()
    {
        TerminalGraphics.Current = new TerminalGraphics(false);
        var overlay = new SixelOverlay();
        var splash = await CreateAsync(overlay);

        splash.Draw(new MockBuffer(new Size(100, 40)));

        Assert.IsNull(overlay.Current);
        Assert.AreEqual(8, (int)splash.ViewSize.Height);
    }
}
