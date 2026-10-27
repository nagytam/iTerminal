using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs.Translator;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Translation;
using Microsoft.Extensions.Options;
using Moq;
using iTerminal.Core.Test.Services.Translation;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs.Translator;

[TestClass]
public class TranslatorDialogTest
{
    private sealed class MemorySettings : ISettingsService
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool IsDirty => false;
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
        public void Set(string key, string value) => Values[key] = value;
        public void Write() { }
    }

    private static (TranslatorDialog Dialog, TranslationServiceTest.FakeProvider Api, TranslationServiceTest.FakeProvider Llm, MemorySettings Settings) Create(
        Action<TranslationServiceTest.FakeProvider, TranslationServiceTest.FakeProvider>? setup = null, MemorySettings? settings = null)
    {
        var api = new TranslationServiceTest.FakeProvider("api");
        var llm = new TranslationServiceTest.FakeProvider("llm");
        setup?.Invoke(api, llm);
        settings ??= new MemorySettings();
        var service = new TranslationService(new LanguageDetector(), [api, llm]);
        var dialog = new TranslatorDialog(service, settings, Options.Create(new TranslationOptions()), TestTheme());
        return (dialog, api, llm, settings);
    }

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    [TestMethod]
    public async Task Tab_LeavesTheInputPane_AndTheTranslationCanBeReachedFromTheKeyboard()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.Input.SetFocus();
        Assert.IsTrue(dialog.Input.IsFocused);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab));

        Assert.IsTrue(dialog.Output.IsFocused, "Tab from the input went to the translation");
        Assert.AreEqual(0, dialog.Input.TextModel.Lines.Count(line => line.Contains('\t')), "no tab character was typed into the text");
    }

    private static async Task SettleAsync(TranslatorDialog dialog)
    {
        await dialog.RequestTask;
        dialog.ApplyPendingForTest();
    }

    private static void DrawChildren(IControl parent, IBuffer buffer)
    {
        foreach (var control in parent.Controls.AsSnapshot().Where(c => c.IsVisible))
        {
            control.Draw(buffer);
            DrawChildren(control, buffer);
        }
    }

    private static string Drawn(TranslatorDialog dialog, int width = 120, int height = 40)
    {
        var buffer = new ScreenBuffer(width, height);
        // Twice, so text left over from the first frame would show; the dialog draws itself, its children draw themselves.
        for (int pass = 0; pass < 2; pass++)
        {
            dialog.Draw(buffer);
            DrawChildren(dialog, buffer);
        }

        return string.Join("\n", Enumerable.Range(0, height).Select(buffer.Line));
    }

    [TestMethod]
    public async Task ItOpensWithDetectLanguageAndEnglishAndShowsTheKeys()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);

        Assert.AreEqual("auto", dialog.FromBox.SelectedItem!.Code);
        Assert.AreEqual("en", dialog.ToBox.SelectedItem!.Code);
        Assert.AreEqual(TranslationEngine.Auto, dialog.EngineBox.SelectedItem!.Engine);
        string screen = Drawn(dialog);
        StringAssert.Contains(screen, "F5:translate");
        StringAssert.Contains(screen, "Detect language");
        StringAssert.Contains(screen, "Translation");
    }

    [TestMethod]
    public async Task F5TranslatesInTheBackgroundAndShowsTheResultAndWhatWasDone()
    {
        var (dialog, api, _, _) = Create((api, _) => api.ReportedSource = "de");
        await ShowAsync(dialog);
        dialog.InputText = "Guten Tag";
        dialog.ToBox.SelectedIndex = dialog.ToBox.Items.ToList().FindIndex(l => l.Code == "fr");

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(ConsoleKey.F5)));
        await SettleAsync(dialog);

        Assert.AreEqual("<Guten Tag>", dialog.TranslationText);
        Assert.IsFalse(dialog.IsBusy);
        Assert.AreEqual("Guten Tag", api.Calls[0].Text);
        Assert.IsNull(api.Calls[0].Source);
        Assert.AreEqual("fr", api.Calls[0].Target);
        StringAssert.Contains(dialog.Status, "German (detected) → French");
        StringAssert.Contains(dialog.Status, "Translation API");
        StringAssert.Contains(Drawn(dialog), "<Guten Tag>");
    }

    [TestMethod]
    public async Task AFailureIsShownInWordsAndLeavesTheOldTranslation()
    {
        var (dialog, api, llm, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "Hello";
        dialog.StartTranslate();
        await SettleAsync(dialog);
        api.Failure = new TranslationException("Translation API is unreachable.");
        llm.Failure = new TranslationException("AI model failed: timeout");

        dialog.StartTranslate();
        await SettleAsync(dialog);

        StringAssert.Contains(dialog.Status, "unreachable");
        StringAssert.Contains(dialog.Status, "timeout");
        Assert.AreEqual("<Hello>", dialog.TranslationText);
    }

    [TestMethod]
    public async Task NothingToTranslateSaysSoWithoutAnyRequest()
    {
        var (dialog, api, _, _) = Create();
        await ShowAsync(dialog);

        dialog.StartTranslate();

        StringAssert.Contains(dialog.Status, "Type or paste");
        Assert.AreEqual(0, api.Calls.Count);
    }

    [TestMethod]
    public async Task ARequestIsNotAppliedUntilTheIdlePassSoNoWorkerTouchesTheScreen()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "Hello";

        dialog.StartTranslate();
        await dialog.RequestTask;

        Assert.AreEqual(string.Empty, dialog.TranslationText);
        dialog.ApplyPendingForTest();
        Assert.AreEqual("<Hello>", dialog.TranslationText);
    }

    [TestMethod]
    public async Task OnlyTheNewestRequestLands()
    {
        var (dialog, api, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "first";
        dialog.StartTranslate();
        await dialog.RequestTask;
        dialog.InputText = "second";
        dialog.StartTranslate();
        await dialog.RequestTask;

        dialog.ApplyPendingForTest();

        Assert.AreEqual("<second>", dialog.TranslationText);
        Assert.AreEqual(2, api.Calls.Count);
    }

    [TestMethod]
    public async Task F7DetectsTheLanguageAndTheInfoLineSaysIt()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "これは日本語の文章です。ひらがなとカタカナがあります。";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));
        await SettleAsync(dialog);

        StringAssert.Contains(dialog.Status, "Detected Japanese");
        StringAssert.Contains(dialog.InfoLine(), "Japanese");
    }

    [TestMethod]
    public async Task BeforeAnythingIsAskedTheInfoLineShowsTheOfflineGuess()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "Der Hund ist nicht in dem Haus, aber ich habe ihn mit der Katze gesehen.";

        StringAssert.Contains(dialog.InfoLine(), "German (a guess)");
    }

    [TestMethod]
    public async Task F6SwapsTheLanguagesAndBringsTheTranslationBack()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.FromBox.SelectedIndex = dialog.FromBox.Items.ToList().FindIndex(l => l.Code == "de");
        dialog.ToBox.SelectedIndex = dialog.ToBox.Items.ToList().FindIndex(l => l.Code == "fr");
        dialog.InputText = "Guten Tag";
        dialog.StartTranslate();
        await SettleAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F6));

        Assert.AreEqual("fr", dialog.FromBox.SelectedItem!.Code);
        Assert.AreEqual("de", dialog.ToBox.SelectedItem!.Code);
        Assert.AreEqual("<Guten Tag>", dialog.InputText);
        Assert.AreEqual(string.Empty, dialog.TranslationText);
    }

    [TestMethod]
    public async Task F6WithNoKnownSourceAsksForOne()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "Xylophone";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F6));

        StringAssert.Contains(dialog.Status, "Choose the From language");
        Assert.AreEqual("auto", dialog.FromBox.SelectedItem!.Code);
    }

    [TestMethod]
    public async Task F8ClearsBothTexts()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "Hello";
        dialog.StartTranslate();
        await SettleAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));
        Assert.AreEqual("Hello", dialog.InputText, "the first F8 only asks");
        StringAssert.Contains(dialog.Status, "Press F8 again");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        Assert.AreEqual(string.Empty, dialog.InputText);
        Assert.AreEqual(string.Empty, dialog.TranslationText);
        Assert.AreEqual(0, dialog.Output.TextModel.Lines.Count);
    }

    [TestMethod]
    public async Task AnotherKeyWithdrawsTheClear_SoTheNextF8AsksAgain()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = "Hello";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        Assert.AreEqual("Hello", dialog.InputText);
        StringAssert.Contains(dialog.Status, "Press F8 again");
    }

    [TestMethod]
    public async Task F8WithNothingTypedClearsAtOnce_ThereIsNothingToLose()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        dialog.InputText = string.Empty;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        Assert.AreEqual(string.Empty, dialog.InputText);
        Assert.IsFalse(dialog.Status.Contains("Press F8 again"));
    }

    [TestMethod]
    public async Task TheChoicesAreRememberedForTheNextTime()
    {
        var settings = new MemorySettings();
        var (first, _, _, _) = Create(settings: settings);
        await ShowAsync(first);
        first.FromBox.SelectedIndex = first.FromBox.Items.ToList().FindIndex(l => l.Code == "ja");
        first.ToBox.SelectedIndex = first.ToBox.Items.ToList().FindIndex(l => l.Code == "de");
        first.EngineBox.SelectedIndex = 2;
        first.Close();

        var (second, _, _, _) = Create(settings: settings);
        await ShowAsync(second);

        Assert.AreEqual("ja", second.FromBox.SelectedItem!.Code);
        Assert.AreEqual("de", second.ToBox.SelectedItem!.Code);
        Assert.AreEqual(TranslationEngine.Llm, second.EngineBox.SelectedItem!.Engine);
    }

    [TestMethod]
    public async Task AnUnusableEngineIsSaidOnTheInfoLine()
    {
        var (dialog, _, _, _) = Create((api, llm) =>
        {
            api.UnavailableReason = "no translation API set up";
            llm.UnavailableReason = "AI is off (AI:Provider)";
        });
        await ShowAsync(dialog);

        StringAssert.Contains(dialog.InfoLine(), "not available");
        StringAssert.Contains(dialog.InfoLine(), "AI is off");
    }

    [TestMethod]
    public async Task TheTranslationIsWrappedToThePaneInsteadOfScrollingSideways()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog, new Size(80, 30));
        dialog.InputText = string.Join(' ', Enumerable.Repeat("word", 40));
        dialog.StartTranslate();
        await SettleAsync(dialog);

        long width = dialog.Output.ViewSize.Width;
        Assert.IsTrue(dialog.Output.TextModel.Lines.Count > 1);
        Assert.IsTrue(dialog.Output.TextModel.Lines.AsSnapshot().All(line => line.Length <= width), "no line is wider than the pane");
    }

    [TestMethod]
    [DataRow("a b c d e f", 5, "a b c|d e f")]
    [DataRow("abcdefghij", 4, "abcd|efgh|ij")]
    [DataRow("one\n\ntwo", 10, "one||two")]
    [DataRow("", 10, "")]
    public void WrapBreaksAtSpacesAndHardWhereThereAreNone(string text, int width, string expected)
    {
        Assert.AreEqual(expected, string.Join('|', TranslatorDialog.Wrap(text, width)));
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var (dialog, _, _, _) = Create();
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task ItFitsAnEightyByTwentyFourScreen()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog, new Size(80, 24));

        string screen = Drawn(dialog, 80, 24);

        StringAssert.Contains(screen, "F5:translate");
        Assert.IsTrue(dialog.Input.ViewSize.Height >= 1 && dialog.Output.ViewSize.Height >= 1);
    }
}
