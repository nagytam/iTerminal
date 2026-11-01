using iConsole.Controls;
using iConsole.Data;
using iConsole.Prompts;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Prompts;

[TestClass]
public class PromptDialogsTest
{
    private readonly TestThemeService _theme = new();

    /// <summary>An 80×24 root for the dialog to sit on, as the prompt service would give it.</summary>
    private Control Root() => new(_theme) { ViewSize = new Size(80, 24) };

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static KeyEvent Char(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static KeyEvent Enter() => new('\r', ConsoleKey.Enter, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Selection_EnterChoosesTheHighlightedItem()
    {
        var root = Root();
        var dialog = new SelectionPromptDialog<string>(new SelectionPrompt<string>("Fruit?", ["Apple", "Banana"]), _theme);
        dialog.Parent = root;
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Enter());

        Assert.AreEqual("Banana", await dialog.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Selection_TypingFiltersAndEnterChoosesTheFirstMatch()
    {
        var root = Root();
        var dialog = new SelectionPromptDialog<string>(new SelectionPrompt<string>("Fruit?", ["Apple", "Banana", "Cherry"]), _theme);
        dialog.Parent = root;
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Char('c'));
        await dialog.HandleKeyEventAsync(Enter());

        Assert.AreEqual("Cherry", await dialog.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Selection_EnterWithNothingMatching_SaysSoAndStaysOpen()
    {
        var root = Root();
        var dialog = new SelectionPromptDialog<string>(new SelectionPrompt<string>("Fruit?", ["Apple"]), _theme);
        dialog.Parent = root;
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Char('z'));
        await dialog.HandleKeyEventAsync(Enter());

        Assert.IsFalse(dialog.Result.IsCompleted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Selection_EscCancels_ToNull()
    {
        var root = Root();
        var dialog = new SelectionPromptDialog<string>(new SelectionPrompt<string>("Fruit?", ["Apple"]), _theme);
        dialog.Parent = root;
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsNull(await dialog.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MultiSelection_SpaceMarks_EnterConfirmsTheMarked()
    {
        var root = Root();
        var dialog = new MultiSelectionPromptDialog<string>(new MultiSelectionPrompt<string>("Fruit?", ["Apple", "Banana", "Cherry"]), _theme);
        dialog.Parent = root;
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        await dialog.HandleKeyEventAsync(Enter());

        CollectionAssert.AreEqual(new[] { "Apple", "Cherry" }, (await dialog.Result)!.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MultiSelection_EnterBelowTheMinimum_IsRefused()
    {
        var root = Root();
        var prompt = new MultiSelectionPrompt<string>("Fruit?", ["Apple", "Banana"]) { MinimumSelections = 2 };
        var dialog = new MultiSelectionPromptDialog<string>(prompt, _theme);
        dialog.Parent = root;
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        await dialog.HandleKeyEventAsync(Enter());

        Assert.IsFalse(dialog.Result.IsCompleted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Confirmation_YesAndNo_AnswerDirectly()
    {
        var root = Root();
        var yes = new ConfirmationPromptDialog(new ConfirmationPrompt("Sure?"), _theme) { Parent = root };
        yes.OnInitializeLayout();
        await yes.HandleKeyEventAsync(Char('y'));
        Assert.IsTrue(await yes.Result);

        var no = new ConfirmationPromptDialog(new ConfirmationPrompt("Sure?"), _theme) { Parent = root };
        no.OnInitializeLayout();
        await no.HandleKeyEventAsync(Char('N'));
        Assert.IsFalse(await no.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Confirmation_EnterTakesTheDefault()
    {
        var root = Root();
        var dialog = new ConfirmationPromptDialog(new ConfirmationPrompt("Sure?") { DefaultValue = false }, _theme) { Parent = root };
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Enter());

        Assert.IsFalse(await dialog.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Confirmation_EnterWithoutADefault_DoesNothing()
    {
        var root = Root();
        var dialog = new ConfirmationPromptDialog(new ConfirmationPrompt("Sure?"), _theme) { Parent = root };
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Enter());

        Assert.IsFalse(dialog.Result.IsCompleted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Text_EnterAcceptsAValidValue()
    {
        var root = Root();
        var beep = new Mock<IBeepService>();
        var dateTime = new Mock<IDateTimeProviderService>();
        var dialog = new TextPromptDialog<int>(new TextPrompt<int>("Age?") { DefaultValue = "41" }, beep.Object, dateTime.Object, _theme)
        {
            Parent = root,
        };
        dialog.OnInitializeLayout();
        Assert.AreEqual("41", dialog.Text);

        await dialog.HandleKeyEventAsync(Enter());

        Assert.AreEqual(41, await dialog.Result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Text_EnterWithAnInvalidValue_ShowsTheReasonAndStaysOpen()
    {
        var root = Root();
        var dialog = new TextPromptDialog<int>(new TextPrompt<int>("Age?") { DefaultValue = "x" }, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), _theme)
        {
            Parent = root,
        };
        dialog.OnInitializeLayout();

        await dialog.HandleKeyEventAsync(Enter());

        Assert.IsFalse(dialog.Result.IsCompleted);
        Assert.AreEqual("'x' is not a valid Int32.", dialog.Message);
    }
}
