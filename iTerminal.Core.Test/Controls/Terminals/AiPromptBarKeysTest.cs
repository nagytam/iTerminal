using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.Extensions.Options;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls.Terminals;

/// <summary>
/// The prompt bar sits beside the panels and receives every key they decline. Only a focused bar may act on them.
/// </summary>
[TestClass]
public class AiPromptBarKeysTest
{
    private static (AiPromptBar Bar, AiChatModel Model) Create()
    {
        var theme = TestTheme();
        var model = new AiChatModel { IsAvailable = true, Input = "draft question" };
        var bar = new AiPromptBar(model, Mock.Of<IAiChatService>(), new AiModelDialog(model, Options.Create(new AiOptions()), theme), Mock.Of<IDateTimeProviderService>(), theme);
        return (bar, model);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnfocusedBar_DoesNotTakeTypedCharacters()
    {
        var (bar, model) = Create();

        await bar.HandleKeyEventAsync(new KeyEvent('x', ConsoleKey.X, false, false, false));

        Assert.AreEqual("draft question", model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnfocusedBar_DoesNotClearOnEscape()
    {
        var (bar, model) = Create();

        await bar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual("draft question", model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnfocusedBar_DoesNotEraseOnBackspace()
    {
        var (bar, model) = Create();

        await bar.HandleKeyEventAsync(new KeyEvent('\b', ConsoleKey.Backspace, false, false, false));

        Assert.AreEqual("draft question", model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFocusedBar_EscapeClearsTheInput()
    {
        var (bar, model) = Create();
        bar.SetFocus();

        await bar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(string.Empty, model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyHints_ListTheBarsKeys_OnlyWhileTheModelIsAvailable()
    {
        var (bar, model) = Create();
        var keys = bar.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.AreEqual(new[] { "Enter", "Esc", "Up/Down", "PageUp/PageDown" }, keys);

        model.IsAvailable = false;
        Assert.AreEqual(0, bar.GetKeyHints().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFocusedBar_TypingAppendsToTheInput()
    {
        var (bar, model) = Create();
        bar.SetFocus();

        await bar.HandleKeyEventAsync(new KeyEvent('!', ConsoleKey.D1, false, false, false));

        Assert.AreEqual("draft question!", model.Input);
    }
}
