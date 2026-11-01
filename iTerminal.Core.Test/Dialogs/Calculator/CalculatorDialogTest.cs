using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Calculator;
using iTerminal.Core.Model.Calculator;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Calculator;

[TestClass]
public class CalculatorDialogTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return mock.Object;
    }

    private static async Task<Control> ShowAsync(CalculatorDialog dialog)
    {
        var themeService = CreateThemeService();
        var root = new Control(themeService) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        return root;
    }

    private static KeyEvent Key(char keyChar, ConsoleKey consoleKey = default) =>
        new(keyChar, consoleKey, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowAsync_StartsInBasicModeWithFirstButtonFocused()
    {
        var dialog = new CalculatorDialog(CreateThemeService());

        await ShowAsync(dialog);

        Assert.AreEqual(CalculatorMode.Basic, dialog.EngineForTest.Mode);
        var firstButton = dialog.TabForTest.TabPages[0].Controls.AsSnapshot().OfType<Button>().First();
        Assert.AreEqual("MC", firstButton.Title);
        Assert.IsTrue(firstButton.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_DigitsOperatorAndEnter_ComputesResult()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key('5'));
        await dialog.HandleKeyEventAsync(Key('+'));
        await dialog.HandleKeyEventAsync(Key('3'));
        await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.Enter));

        Assert.AreEqual("8", dialog.EngineForTest.Display);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_Backspace_RemovesLastDigit()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Key('1'));
        await dialog.HandleKeyEventAsync(Key('2'));

        await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.Backspace));

        Assert.AreEqual("1", dialog.EngineForTest.Display);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_Delete_ClearsDisplay()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Key('7'));

        await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.Delete));

        Assert.AreEqual("0", dialog.EngineForTest.Display);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_LowercaseC_ClearsDisplayInBasicMode()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Key('7'));

        await dialog.HandleKeyEventAsync(Key('c'));

        Assert.AreEqual("0", dialog.EngineForTest.Display);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_Escape_ClosesDialog()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        var root = await ShowAsync(dialog);

        var handled = await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.Escape));

        Assert.IsTrue(handled);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_RightArrow_SwitchesToScientificMode()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));

        Assert.AreEqual(1, dialog.TabForTest.FocusedTabPage);
        Assert.AreEqual(CalculatorMode.Scientific, dialog.EngineForTest.Mode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_RightArrowTwice_SwitchesToProgrammingMode()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));

        Assert.AreEqual(2, dialog.TabForTest.FocusedTabPage);
        Assert.AreEqual(CalculatorMode.Programming, dialog.EngineForTest.Mode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_LeftArrowBackFromScientific_ReturnsToBasicMode()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.LeftArrow, false, false, false));

        Assert.AreEqual(CalculatorMode.Basic, dialog.EngineForTest.Mode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_HexDigitInProgrammingHexMode_EntersDigitInsteadOfClearing()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));
        dialog.EngineForTest.SetNumeralBase(NumeralBase.Hexadecimal);
        await dialog.HandleKeyEventAsync(Key('9'));

        await dialog.HandleKeyEventAsync(Key('c'));

        Assert.AreEqual("9C", dialog.EngineForTest.Display);
        Assert.AreEqual(NumeralBase.Hexadecimal, dialog.EngineForTest.NumeralBase);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScientificSqrtButton_TriggeredDirectly_UpdatesEngineDisplay()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));
        await dialog.HandleKeyEventAsync(Key('9'));

        var sqrtButton = dialog.TabForTest.TabPages[1].Controls.AsSnapshot().OfType<Button>().First(b => b.Title == "sqrt");
        await sqrtButton.Commands.First().RunAsync(null);

        Assert.AreEqual("3", dialog.EngineForTest.Display);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProgrammingHexDigitButtons_AreDisabledOutsideHexBase()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));

        var buttonA = dialog.TabForTest.TabPages[2].Controls.AsSnapshot().OfType<Button>().First(b => b.Title == "A");

        Assert.IsFalse(buttonA.IsEnabled);

        dialog.EngineForTest.SetNumeralBase(NumeralBase.Hexadecimal);

        Assert.IsTrue(buttonA.IsEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnAModesTab_SwitchesTheEngineAtOnce_NotAtTheNextKey()
    {
        var dialog = new CalculatorDialog(CreateThemeService());
        await ShowAsync(dialog);
        Assert.AreEqual(CalculatorMode.Basic, dialog.EngineForTest.Mode);
        var tab = dialog.TabForTest;

        // The names sit on the row under the tab's top border; try along it until one of them is hit.
        for (long x = tab.AbsolutePosition.X; x < tab.AbsolutePosition.X + tab.ViewSize.Width && tab.FocusedTabPage == 0; x++)
        {
            await dialog.HandleMouseEventAsync(new MouseEvent { Position = new Position(x, tab.AbsolutePosition.Y + 1), LeftClicked = true });
        }

        Assert.AreEqual(1, tab.FocusedTabPage);
        Assert.AreEqual(CalculatorMode.Scientific, dialog.EngineForTest.Mode);
    }
}
