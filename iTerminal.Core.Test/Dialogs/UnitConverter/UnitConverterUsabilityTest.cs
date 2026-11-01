using iConsole.Services;
using iTerminal.Core.Dialogs.UnitConverter;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.UnitConverter;

/// <summary>What the unit converter tells the user about its lists, its keys and a bad value.</summary>
[TestClass]
public class UnitConverterUsabilityTest
{
    private static async Task<UnitConverterDialog> OpenAsync()
    {
        var dialog = new UnitConverterDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);
        return dialog;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EachListIsHeadedWithWhatItChooses()
    {
        // The from and to lists stood side by side with nothing to tell them apart.
        var dialog = await OpenAsync();

        var screen = Render(dialog);

        AssertScreenContains(screen, " Category");
        AssertScreenContains(screen, " From");
        AssertScreenContains(screen, " To");
        AssertScreenContains(screen, "Unit Converter — Length");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFromHeadingSitsOverTheFromList()
    {
        var dialog = await OpenAsync();

        var screen = Render(dialog);
        var heading = screen.Find(" From");

        Assert.IsNotNull(heading);
        Assert.AreEqual((int)dialog.FromUnitListBoxForTest.AbsolutePosition.X, (int)heading!.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AValueThatIsNotANumberSaysSo()
    {
        var dialog = await OpenAsync();

        dialog.InputTextBoxForTest.Text = "abc";
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End));

        Assert.AreEqual("\"abc\" is not a number.", dialog.ResultLine());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyValueInvitesOne()
    {
        var dialog = await OpenAsync();

        dialog.InputTextBoxForTest.Text = string.Empty;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End));

        StringAssert.StartsWith(dialog.ResultLine(), "Type a value in ");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADecimalCommaIsAccepted()
    {
        var dialog = await OpenAsync();

        dialog.InputTextBoxForTest.Text = "1,5";
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End));

        Assert.IsFalse(string.IsNullOrEmpty(dialog.EngineForTest.ResultText), "1,5 is a number");
        StringAssert.StartsWith(dialog.ResultLine(), "1,5 ");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSwapKeyIsNamed()
    {
        var dialog = await OpenAsync();

        AssertScreenContains(Render(dialog), "Ctrl+S:swap units");
    }
}
