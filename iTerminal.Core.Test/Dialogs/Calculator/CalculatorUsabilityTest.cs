using iConsole.Controls.Buttons;
using iConsole.Data;
using iTerminal.Core.Dialogs.Calculator;
using iTerminal.Core.Model.Calculator;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Calculator;

/// <summary>
/// What the calculator tells the user and how its keys behave together with its buttons. The engine
/// itself is covered by <c>CalculatorEngineTest</c>; the basic key handling by <c>CalculatorDialogTest</c>.
/// </summary>
[TestClass]
public class CalculatorUsabilityTest
{
    private static async Task<CalculatorDialog> OpenAsync()
    {
        var dialog = new CalculatorDialog(TestTheme());
        await ShowAsync(dialog);
        return dialog;
    }

    private static async Task TypeKeysAsync(CalculatorDialog dialog, string keys)
    {
        foreach (var c in keys)
        {
            await dialog.HandleKeyEventAsync(new KeyEvent(c, ConsoleKey.Oem1, false, false, false));
        }
    }

    private static Button FocusedButton(CalculatorDialog dialog) =>
        dialog.TabForTest.TabPages[dialog.TabForTest.FocusedTabPage].Controls.AsSnapshot().OfType<Button>().Single(b => b.IsFocused);

    [TestMethod]
    public async Task TheKeysAreListedOnScreen()
    {
        // Enter, Delete, c, % and ^ worked but nothing on screen said so.
        var dialog = await OpenAsync();

        var screen = Render(dialog);

        AssertScreenContains(screen, "Enter:=");
        AssertScreenContains(screen, "Del/c:clear");
        AssertScreenContains(screen, "Space:press button");
    }

    [TestMethod]
    public async Task TheHintRowDoesNotOverlapTheButtons()
    {
        var dialog = await OpenAsync();

        var tab = dialog.TabForTest;
        var hintRow = (int)dialog.ViewSize.Height - 2;

        Assert.IsTrue(tab.Position.Y + tab.ViewSize.Height <= hintRow, "the tab ends above the hint row");
    }

    [TestMethod]
    public async Task CaretRaisesToAPower()
    {
        var dialog = await OpenAsync();

        await TypeKeysAsync(dialog, "2^10");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("1024", dialog.EngineForTest.Display);
    }

    [TestMethod]
    public async Task PercentKeyIsApplied()
    {
        var dialog = await OpenAsync();

        await TypeKeysAsync(dialog, "50%");

        Assert.AreEqual("0.5", dialog.EngineForTest.Display);
    }

    [TestMethod]
    public async Task EnterIsEqualsEvenWithAButtonFocused()
    {
        // The first button (MC) has focus from the start; Enter must still mean "=".
        var dialog = await OpenAsync();
        Assert.AreEqual("MC", FocusedButton(dialog).Title);

        await TypeKeysAsync(dialog, "6*7");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("42", dialog.EngineForTest.Display);
    }

    [TestMethod]
    public async Task SpacePressesTheFocusedButton()
    {
        var dialog = await OpenAsync();
        await TypeKeysAsync(dialog, "9");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        Assert.AreEqual("AC", FocusedButton(dialog).Title);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));

        Assert.AreEqual("0", dialog.EngineForTest.Display);
    }

    [TestMethod]
    public async Task MemoryIsFlaggedInTheHeader()
    {
        var dialog = await OpenAsync();
        await TypeKeysAsync(dialog, "5");

        var memoryAdd = dialog.TabForTest.TabPages[0].Controls.AsSnapshot().OfType<Button>().Single(b => b.Title == "M+");
        await memoryAdd.TriggerHotKeyAsync();

        AssertScreenContains(Render(dialog), "M  Calculator");
    }

    [TestMethod]
    public async Task AnErrorIsShownInTheErrorColour()
    {
        var dialog = await OpenAsync();
        await TypeKeysAsync(dialog, "1/0");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsTrue(dialog.EngineForTest.IsError);
        var screen = Render(dialog);
        var display = screen.CellOf(dialog.EngineForTest.Display);
        Assert.AreEqual(TestTheme().CurrentColorScheme.Error, display.ForegroundColor);
    }

    [TestMethod]
    public async Task ProgrammingModeShowsTheNumeralBase()
    {
        var dialog = await OpenAsync();
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow, control: true));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow, control: true));

        Assert.AreEqual(CalculatorMode.Programming, dialog.EngineForTest.Mode);
        AssertScreenContains(Render(dialog), "[Decimal]");
    }
}

[TestClass]
public class CalculatorTabPagesTest
{
    private static List<Button> Buttons(CalculatorGridTabPage page) => [.. page.Controls.AsSnapshot().OfType<Button>()];

    private static void Press(CalculatorGridTabPage page, string title) =>
        Buttons(page).First(b => b.Title == title).TriggerHotKeyAsync().GetAwaiter().GetResult();

    [TestMethod]
    public void BasicPageHasAFourColumnKeypad()
    {
        var engine = new CalculatorEngine();
        var page = new CalculatorBasicTabPage(engine, () => { }, TestTheme());

        CollectionAssert.AreEqual(
            new[] { "MC", "MR", "M+", "M-", "AC", "CE", "Back", "/", "7", "8", "9", "*", "4", "5", "6", "-", "1", "2", "3", "+", "+/-", "0", ".", "=" },
            Buttons(page).Select(b => b.Title).ToArray());
    }

    [TestMethod]
    public void ButtonsAreLaidOutInAGridThatFillsThePage()
    {
        var page = new CalculatorBasicTabPage(new CalculatorEngine(), () => { }, TestTheme())
        {
            ViewSize = new Size(40, 12),
        };

        page.OnInitializeLayout();

        var buttons = Buttons(page);
        Assert.AreEqual(new Position(0, 0), buttons[0].Position);
        Assert.AreEqual(new Position(10, 0), buttons[1].Position, "four columns of ten");
        Assert.AreEqual(new Position(0, 2), buttons[4].Position, "six rows of two");
        Assert.AreEqual(new Size(9, 1), buttons[0].ViewSize, "a column's width less a gap");
        Assert.IsTrue(buttons.All(b => b.Position.X + b.ViewSize.Width <= 40 && b.Position.Y < 12));
    }

    [TestMethod]
    public void PressingAButtonRunsItsActionAndReportsTheChange()
    {
        var engine = new CalculatorEngine();
        var changes = 0;
        var page = new CalculatorBasicTabPage(engine, () => changes++, TestTheme());

        Press(page, "7");
        Press(page, "+");
        Press(page, "5");
        Press(page, "=");

        Assert.AreEqual("12", engine.Display);
        Assert.AreEqual(4, changes, "the dialog repaints after every button");
    }

    [TestMethod]
    public void ScientificPageAngleButtonShowsAndTogglesTheUnit()
    {
        var engine = new CalculatorEngine();
        var page = new CalculatorScientificTabPage(engine, () => { }, TestTheme());
        var angle = Buttons(page).Single(b => b.Title is "Deg" or "Rad");
        var before = angle.Title;

        angle.TriggerHotKeyAsync().GetAwaiter().GetResult();

        Assert.AreNotEqual(before, angle.Title);
    }

    [TestMethod]
    public void ScientificPageAppliesFunctions()
    {
        var engine = new CalculatorEngine();
        var page = new CalculatorScientificTabPage(engine, () => { }, TestTheme());

        Press(page, "9");
        Press(page, "sqrt");

        Assert.AreEqual("3", engine.Display);
    }

    [TestMethod]
    public void ProgrammingPageMarksTheActiveBase()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        var page = new CalculatorProgrammingTabPage(engine, () => { }, TestTheme());

        Press(page, "HEX");

        var titles = Buttons(page).Select(b => b.Title).ToList();
        CollectionAssert.Contains(titles, "[HEX]");
        CollectionAssert.Contains(titles, "DEC");
    }

    [TestMethod]
    public void ProgrammingPageDisablesDigitsTheBaseDoesNotHave()
    {
        var engine = new CalculatorEngine();
        engine.SetMode(CalculatorMode.Programming);
        var page = new CalculatorProgrammingTabPage(engine, () => { }, TestTheme());

        Press(page, "BIN");

        var buttons = Buttons(page);
        Assert.IsTrue(buttons.Single(b => b.Title == "1").IsEnabled);
        Assert.IsTrue(buttons.Single(b => b.Title == "0").IsEnabled);
        Assert.IsFalse(buttons.Single(b => b.Title == "2").IsEnabled);
        Assert.IsFalse(buttons.Single(b => b.Title == "9").IsEnabled);
        Assert.IsFalse(buttons.Single(b => b.Title == "F").IsEnabled);
    }
}
