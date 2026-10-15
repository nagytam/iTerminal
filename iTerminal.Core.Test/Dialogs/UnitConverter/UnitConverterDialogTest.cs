using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.UnitConverter;
using iTerminal.Core.Model.UnitConverter;
using Moq;

namespace iTerminal.Core.Test.Dialogs.UnitConverter;

[TestClass]
public class UnitConverterDialogTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return mock.Object;
    }

    private static UnitConverterDialog CreateDialog(IThemeService themeService) =>
        new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), themeService);

    private static async Task<Control> ShowAsync(UnitConverterDialog dialog)
    {
        var themeService = CreateThemeService();
        var root = new Control(themeService) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        return root;
    }

    private static KeyEvent DownArrow() => new('\0', ConsoleKey.DownArrow, false, false, false);

    [TestMethod]
    public async Task ShowAsync_DefaultsToLengthCategoryWithFirstTwoUnitsSelected()
    {
        var dialog = CreateDialog(CreateThemeService());

        await ShowAsync(dialog);

        Assert.AreEqual(UnitCategory.Length, dialog.EngineForTest.Category);
        Assert.AreEqual(0, dialog.EngineForTest.FromUnitIndex);
        Assert.AreEqual(1, dialog.EngineForTest.ToUnitIndex);
        Assert.AreEqual("0.1", dialog.EngineForTest.ResultText);
    }

    [TestMethod]
    public async Task ShowAsync_FocusesCategoryListBoxFirst()
    {
        var dialog = CreateDialog(CreateThemeService());

        await ShowAsync(dialog);

        Assert.IsTrue(dialog.CategoryListBoxForTest.IsFocused);
    }

    [TestMethod]
    public async Task TypingInInputTextBox_RecomputesResult()
    {
        var dialog = CreateDialog(CreateThemeService());
        await ShowAsync(dialog);
        dialog.InputTextBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(new KeyEvent('5', ConsoleKey.D5, false, false, false));

        Assert.AreEqual("15", dialog.InputTextBoxForTest.Text);
        Assert.AreEqual("15", dialog.EngineForTest.InputText);
        Assert.AreEqual("1.5", dialog.EngineForTest.ResultText);
    }

    [TestMethod]
    public async Task Escape_ClosesDialog()
    {
        var dialog = CreateDialog(CreateThemeService());
        var root = await ShowAsync(dialog);

        var handled = await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsTrue(handled);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task NavigatingCategoryListDown_SwitchesCategoryAndRepopulatesUnitLists()
    {
        var dialog = CreateDialog(CreateThemeService());
        await ShowAsync(dialog);
        dialog.CategoryListBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(DownArrow());

        Assert.AreEqual(UnitCategory.Area, dialog.EngineForTest.Category);
        Assert.AreEqual(UnitCatalog.GetUnits(UnitCategory.Area).Count, dialog.FromUnitListBoxForTest.Count);
        Assert.AreEqual(UnitCatalog.GetUnits(UnitCategory.Area).Count, dialog.ToUnitListBoxForTest.Count);
    }

    [TestMethod]
    public async Task NavigatingFromUnitListDown_UpdatesEngineFromUnitIndex()
    {
        var dialog = CreateDialog(CreateThemeService());
        await ShowAsync(dialog);
        dialog.FromUnitListBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(DownArrow());
        await dialog.HandleKeyEventAsync(DownArrow());

        Assert.AreEqual(2, dialog.EngineForTest.FromUnitIndex);
    }

    [TestMethod]
    public async Task NavigatingToUnitListDown_UpdatesEngineToUnitIndex()
    {
        var dialog = CreateDialog(CreateThemeService());
        await ShowAsync(dialog);
        dialog.ToUnitListBoxForTest.SetFocus();

        await dialog.HandleKeyEventAsync(DownArrow());

        Assert.AreEqual(2, dialog.EngineForTest.ToUnitIndex);
    }

    [TestMethod]
    public async Task SwapButton_TriggeredDirectly_SwapsFromAndToUnitsAndListCursors()
    {
        var dialog = CreateDialog(CreateThemeService());
        await ShowAsync(dialog);

        await dialog.SwapButtonForTest.Commands.First().RunAsync(null);

        Assert.AreEqual(1, dialog.EngineForTest.FromUnitIndex);
        Assert.AreEqual(0, dialog.EngineForTest.ToUnitIndex);
        Assert.AreEqual(1, dialog.FromUnitListBoxForTest.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual(0, dialog.ToUnitListBoxForTest.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task SwitchingToTemperatureCategory_ComputesUsingOffsetFormula()
    {
        var dialog = CreateDialog(CreateThemeService());
        await ShowAsync(dialog);
        dialog.CategoryListBoxForTest.SetFocus();

        for (var i = 0; i < (int)UnitCategory.Temperature; i++)
        {
            await dialog.HandleKeyEventAsync(DownArrow());
        }

        Assert.AreEqual(UnitCategory.Temperature, dialog.EngineForTest.Category);
        Assert.AreEqual(0, dialog.EngineForTest.FromUnitIndex); // Celsius
        Assert.AreEqual(1, dialog.EngineForTest.ToUnitIndex);   // Fahrenheit
        Assert.AreEqual(33.8, double.Parse(dialog.EngineForTest.ResultText), 1e-9); // 1 C -> F
    }
}
