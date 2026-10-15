using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Portfolio;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Portfolio;

[TestClass]
public class PortfolioSetupWizardDialogTest
{
    private static PortfolioSetupWizardDialog CreateDialog(ISettingsService? settingsService = null)
    {
        return new PortfolioSetupWizardDialog(
            Mock.Of<IPortfolioService>(),
            settingsService ?? new SettingsService(),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            NullThemeService.Instance);
    }

    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var dialog = CreateDialog();

        Assert.IsNotNull(dialog);
        Assert.IsTrue(dialog.IsModal);
    }

    [TestMethod]
    public async Task NextButton_MovesToStocksStep_AndCheckBoxesBecomeFocusable()
    {
        var dialog = CreateDialog();
        await dialog.ShowAsync();

        // Ctrl+N triggers the Next button's hotkey regardless of current focus, mirroring how a
        // user would actually press it in the real app.
        var handled = await dialog.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.N, shift: false, alt: false, control: true));
        Assert.IsTrue(handled, "Ctrl+N should trigger the Next button");

        var checkBoxesField = typeof(PortfolioSetupWizardDialog).GetField("_stockCheckBoxes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var checkBoxes = (System.Collections.IList)checkBoxesField.GetValue(dialog)!;
        var firstBox = (iConsole.Controls.CheckBox)((dynamic)checkBoxes[0]!).Item2;

        Assert.IsTrue(firstBox.IsVisible, "the first stock checkbox should become visible after Next");
        Assert.IsTrue(firstBox.CanFocus, "the first stock checkbox should become focusable after Next");
    }

    [TestMethod]
    public async Task StockHotKey_TogglesCheckBox_RegardlessOfFocus()
    {
        var dialog = CreateDialog();
        await dialog.ShowAsync();
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.N, shift: false, alt: false, control: true));

        var checkBoxesField = typeof(PortfolioSetupWizardDialog).GetField("_stockCheckBoxes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var checkBoxes = (System.Collections.IList)checkBoxesField.GetValue(dialog)!;
        // MSFT's hotkey is Ctrl+M (index 1); it is not focused, proving the hotkey doesn't depend
        // on Tab focus (which Dialog.OnInitializeLayout can silently reset on window resize).
        var msftBox = (iConsole.Controls.CheckBox)((dynamic)checkBoxes[1]!).Item2;
        Assert.IsFalse(msftBox.IsFocused, "MSFT should not be the focused control for this to be a meaningful test");

        var handled = await dialog.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.M, shift: false, alt: false, control: true));

        Assert.IsTrue(handled, "Ctrl+M should toggle the MSFT checkbox via its hotkey");
        Assert.IsTrue(msftBox.IsChecked, "MSFT should be checked after its hotkey fires");
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_Escape_ClosesDialog()
    {
        var dialog = CreateDialog();
        await dialog.ShowAsync();

        var handled = await dialog.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        Assert.IsTrue(handled, "Escape should be handled and close the wizard");
    }

    [TestMethod]
    public async Task FinishButton_AddsCheckedHoldingsAndMarksConfigured()
    {
        var portfolioService = new Mock<IPortfolioService>();
        var settingsService = new SettingsService();
        var dialog = new PortfolioSetupWizardDialog(
            portfolioService.Object,
            settingsService,
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            NullThemeService.Instance);
        await dialog.ShowAsync();
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.N, shift: false, alt: false, control: true));

        // Toggle AAPL (Ctrl+A) on, then finish (Ctrl+F).
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.A, shift: false, alt: false, control: true));
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F, shift: false, alt: false, control: true));

        // FinishSetup does its work on a background Task; give it a moment to complete.
        for (var i = 0; i < 50 && settingsService.GetAsBool(PortfolioSetupWizardDialog.ConfiguredSettingKey, false) != true; i++)
        {
            await Task.Delay(20);
        }

        Assert.AreEqual(true, settingsService.GetAsBool(PortfolioSetupWizardDialog.ConfiguredSettingKey, false));
        portfolioService.Verify(s => s.AddHoldingAsync("AAPL", null, 0m, 0m, "USD", null, null), Times.Once);
    }
}
