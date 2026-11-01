using iConsole.Controls;
using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Translator;
using iTerminal.Core.Services.Translation;
using iTerminal.Core.Services;
using Microsoft.Extensions.Options;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class TranslatorDialogCommandTest
{
    private static TranslatorDialog CreateDialog() =>
        new(Mock.Of<ITranslationService>(), Mock.Of<ISettingsService>(), Options.Create(new TranslationOptions()), TestTheme());

    [Timeout(60_000)]
    [TestMethod]
    public void ItIsOnCtrlShiftF4()
    {
        var command = new TranslatorDialogCommand(CreateDialog());

        Assert.AreEqual(ConsoleKey.F4, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
        Assert.IsTrue(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoControlAttachedItDoesNothing()
    {
        await new TranslatorDialogCommand(CreateDialog()).ExecuteAsync(null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithAControlAttachedItOpensTheDialog()
    {
        TranslatorDialog dialog = CreateDialog();
        var root = new Control(TestTheme()) { Name = "Root", ViewSize = new Size(120, 40) };
        var command = new TranslatorDialogCommand(dialog) { Control = root };

        await command.ExecuteAsync(null);

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }
}
