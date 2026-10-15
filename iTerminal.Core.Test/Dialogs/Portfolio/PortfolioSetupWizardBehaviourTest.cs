using iConsole.Services;
using iTerminal.Core.Dialogs.Portfolio;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Portfolio;

/// <summary>The first-run portfolio setup: what each step shows, and what Enter does in it.</summary>
[TestClass]
public class PortfolioSetupWizardBehaviourTest
{
    private static (PortfolioSetupWizardDialog Dialog, Mock<IPortfolioService> Service) Create()
    {
        var service = new Mock<IPortfolioService>();
        var dialog = new PortfolioSetupWizardDialog(service.Object, new SettingsService(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return (dialog, service);
    }

    [TestMethod]
    public async Task StepOneShowsOnlyItsOwnFields()
    {
        // Control.ShowAsync made every child visible: the stock boxes and Finish were drawn over
        // step 1's fields and its Next button.
        var (dialog, _) = Create();

        await ShowAsync(dialog);

        Assert.IsFalse(dialog.IsOnStocksStepForTest);
        Assert.IsTrue(dialog.IsNextVisibleForTest);
        Assert.IsFalse(dialog.IsFinishVisibleForTest);
        Assert.IsFalse(dialog.AreStockBoxesVisibleForTest);
        Assert.IsFalse(dialog.CustomSymbolBoxForTest.IsVisible);

        var screen = Render(dialog);
        AssertScreenContains(screen, "Step 1 of 2");
        AssertScreenDoesNotContain(screen, "AAPL");
    }

    [TestMethod]
    public async Task EnterOnStepOneMovesOn()
    {
        var (dialog, _) = Create();
        await ShowAsync(dialog);
        Assert.IsTrue(dialog.ExchangeBoxForTest.IsFocused, "the first field has focus");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsTrue(dialog.IsOnStocksStepForTest);
        Assert.IsTrue(dialog.AreStockBoxesVisibleForTest);
        Assert.IsFalse(dialog.IsNextVisibleForTest);
        AssertScreenContains(Render(dialog), "Step 2 of 2");
    }

    [TestMethod]
    public async Task EnterInTheCustomSymbolBoxAddsTheSymbol()
    {
        var (dialog, _) = Create();
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        dialog.CustomSymbolBoxForTest.SetFocus();
        dialog.CustomSymbolBoxForTest.Text = "ibm";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        StringAssert.StartsWith(dialog.MessageForTest, "Added IBM.");
        Assert.AreEqual(string.Empty, dialog.CustomSymbolBoxForTest.Text);
    }

    [TestMethod]
    public async Task TheFooterNamesTheKeysOfEachStep()
    {
        var (dialog, _) = Create();
        await ShowAsync(dialog);
        AssertScreenContains(Render(dialog), "Enter or Ctrl+N:next");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        AssertScreenContains(Render(dialog), "Ctrl+F:finish");
    }
}
