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

    [Timeout(60_000)]
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
        Assert.AreEqual(22, (int)splash.ViewSize.Height, "the text makes room for the emblem");
        Assert.IsTrue(overlay.Current.Data.StartsWith("\u001bP", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutSixel_TheSplashIsTextOnly()
    {
        TerminalGraphics.Current = new TerminalGraphics(false);
        var overlay = new SixelOverlay();
        var splash = await CreateAsync(overlay);

        splash.Draw(new MockBuffer(new Size(100, 40)));

        Assert.IsNull(overlay.Current);
        Assert.AreEqual(15, (int)splash.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSplash_ListsTheTerminalProtocolsWithTheirState()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        var splash = await CreateAsync(new SixelOverlay());
        var buffer = new MockBuffer(new Size(100, 40));

        splash.Draw(buffer);

        string text = buffer.ContentAsString;
        Assert.IsTrue(text.Contains("Terminal protocols"), text);
        Assert.IsTrue(text.Contains("Sixel graphics"), text);
        Assert.IsTrue(text.Contains("Clipboard OSC 52"), text);
        Assert.IsTrue(text.Contains("Hyperlinks OSC 8"), text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheProtocolList_FollowsTheSixelFeatureFlag()
    {
        TerminalGraphics.Current = new TerminalGraphics(true, 10, 20, true);
        Func<bool>? saved = TerminalGraphics.EnabledSource;
        try
        {
            TerminalGraphics.EnabledSource = () => true;
            Assert.IsTrue(iConsole.Services.TerminalProtocols.Current().First(p => p.Name == "Sixel graphics").IsEnabled);

            TerminalGraphics.EnabledSource = () => false;
            Assert.IsFalse(iConsole.Services.TerminalProtocols.Current().First(p => p.Name == "Sixel graphics").IsEnabled);
            Assert.IsFalse(TerminalGraphics.Current.IsSixelSupported, "the flag turns Sixel off even where it was detected");
        }
        finally
        {
            TerminalGraphics.EnabledSource = saved;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFeatureFlag_CannotTurnOnATerminalWithoutSixel()
    {
        TerminalGraphics.Current = new TerminalGraphics(false);
        Func<bool>? saved = TerminalGraphics.EnabledSource;
        try
        {
            TerminalGraphics.EnabledSource = () => true;
            Assert.IsFalse(TerminalGraphics.Current.IsSixelSupported);
        }
        finally
        {
            TerminalGraphics.EnabledSource = saved;
        }
    }
}
