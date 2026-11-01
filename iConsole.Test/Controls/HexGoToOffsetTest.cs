using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class HexGoToOffsetTest
{
    private static HexViewer Dump(int length)
    {
        var hex = new HexViewer(new ThemeService())
        {
            Data = Enumerable.Range(0, length).Select(i => (byte)i).ToArray(),
        };
        hex.ViewSize = new Size(60, 6);
        hex.OnInitializeLayout();
        return hex;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOffsetCanBeDecimal_OrHexWithAPrefixOrASuffix()
    {
        Assert.IsTrue(HexViewer.TryParseOffset("32", out long decimalOffset));
        Assert.AreEqual(32, decimalOffset);
        Assert.IsTrue(HexViewer.TryParseOffset("0x1F", out long prefixed));
        Assert.AreEqual(31, prefixed);
        Assert.IsTrue(HexViewer.TryParseOffset("1Fh", out long suffixed));
        Assert.AreEqual(31, suffixed);
        Assert.IsTrue(HexViewer.TryParseOffset("  0X10  ", out long spaced));
        Assert.AreEqual(16, spaced);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NotAnOffset_IsRefused()
    {
        Assert.IsFalse(HexViewer.TryParseOffset("", out _));
        Assert.IsFalse(HexViewer.TryParseOffset("12z", out _));
        Assert.IsFalse(HexViewer.TryParseOffset("-4", out _));
        Assert.IsFalse(HexViewer.TryParseOffset("0xZZ", out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShowOffset_PutsTheCursorOnTheRowOfTheByte()
    {
        var hex = Dump(1000);

        hex.ShowOffset(0x123);

        Assert.AreEqual(0x120, hex.SelectedOffset, "the row of the byte starts at the byte's 16-byte boundary");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShowOffset_PastTheEnd_GoesToTheLastByte()
    {
        var hex = Dump(100);

        hex.ShowOffset(5000);

        Assert.AreEqual(96, hex.SelectedOffset, "the last row of 100 bytes starts at byte 96");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShowOffset_OnAnEmptyDump_DoesNothing()
    {
        var hex = Dump(0);

        hex.ShowOffset(10);

        Assert.AreEqual(0, hex.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoToOffsetDialog_Enter_GoesThereAndCloses()
    {
        var hex = Dump(1000);
        var dialog = new GoToOffsetDialog(hex, Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService());
        dialog.Controls.OfType<TextBox>().Single().Text = "0x200";

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual(0x200, hex.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoToOffsetDialog_NotAnOffset_SaysSoAndStaysPut()
    {
        var hex = Dump(1000);
        var dialog = new GoToOffsetDialog(hex, Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService());
        dialog.Controls.OfType<TextBox>().Single().Text = "nowhere";

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        StringAssert.StartsWith(dialog.Message, "Enter an offset");
        Assert.AreEqual(0, hex.SelectedOffset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCommandIsAvailableOnlyInTheHexDump()
    {
        var hex = Dump(10);
        var command = new ViewerGoToOffsetCommand(hex, Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService());

        hex.IsVisible = true;
        Assert.IsTrue(command.CanExecute());
        hex.IsVisible = false;
        Assert.IsFalse(command.CanExecute());
    }
}
