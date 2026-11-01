using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Dialogs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iConsole.Test.Localization;

/// <summary>
/// The standard Cancel and Close buttons and a prompt's default answers speak the language the user chose. The language is
/// held in a static, so these tests run on their own and put it back.
/// </summary>
[TestClass]
[DoNotParallelize]
public class UiTextTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static string Answers(ErrorPromptDialog dialog)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Button accept = (Button)typeof(ErrorPromptDialog).GetField("_continueButton", flags)!.GetValue(dialog)!;
        Button decline = (Button)typeof(ErrorPromptDialog).GetField("_cancelButton", flags)!.GetValue(dialog)!;
        return accept.Title + "|" + decline.Title;
    }

    private static string HotKeys(ErrorPromptDialog dialog)
    {
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Button accept = (Button)typeof(ErrorPromptDialog).GetField("_continueButton", flags)!.GetValue(dialog)!;
        Button decline = (Button)typeof(ErrorPromptDialog).GetField("_cancelButton", flags)!.GetValue(dialog)!;
        return accept.HotKeyChar + "|" + decline.HotKeyChar;
    }

    private static void Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
    }

    [TestMethod]
    public void WithNoLocalizer_TheEnglishTextShows()
    {
        UiText.Current = null;

        Assert.AreEqual("Cancel", new CancelDialogButton(Theme()).Title);
        Assert.AreEqual("Cancel", new CancelButton(new CancellationTokenSource(), Theme()).Title);
        Assert.AreEqual("Close", new CloseButton(Theme()).Title);
        Assert.AreEqual("Close", new CloseCommand().Title);
    }

    [TestMethod]
    public void TheStandardButtons_FollowTheChosenLanguage()
    {
        Speak("de");

        Assert.AreEqual("Abbrechen", new CancelDialogButton(Theme()).Title);
        Assert.AreEqual("Abbrechen", new CancelButton(new CancellationTokenSource(), Theme()).Title);
        Assert.AreEqual("Schließen", new CloseButton(Theme()).Title);
        Assert.AreEqual("Schließen", new CloseCommand().Title);
    }

    [TestMethod]
    public void AKeyNoLanguageHas_FallsBackToTheGivenText()
    {
        Speak("de");

        Assert.AreEqual("fallback", UiText.Get("No.Such.Key", "fallback"));
    }

    [TestMethod]
    public void APromptsDefaultAnswers_FollowTheChosenLanguage()
    {
        Speak("de");

        Assert.AreEqual("Weiter|Abbrechen", Answers(new ErrorPromptDialog("Titel", "Nachricht", Theme())));
    }

    [TestMethod]
    public void APromptsGivenAnswers_AreKeptAsTheyAre()
    {
        Speak("de");

        Assert.AreEqual("Resume|Stop", Answers(new ErrorPromptDialog("t", "m", Theme(), "Resume", "Stop")));
    }

    [TestMethod]
    public void EveryLanguage_TranslatesTheContinueAnswer()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Speak(language.Code);

            Assert.AreNotEqual("Continue", UiText.Get("Common.Continue", "Continue"), language.Code);
        }
    }

    [TestMethod]
    public void EveryLanguage_TranslatesTheWordsTheKeyHintsUse()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Speak(language.Code);

            foreach (string word in new[] { "Cancel", "Close", "Confirm", "Help", "Back", "Paste", "Undo", "Save", "Stop" })
            {
                string text = UiText.Get("Common." + word, word);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), language.Code + " " + word);
                Assert.AreNotEqual("Common." + word, text);
            }
        }
    }

    [TestMethod]
    public void ATextBoxHint_FollowsTheChosenLanguage()
    {
        Speak("de");
        Control box = iConsole.Test.Controls.SweepControlFactory.Build(typeof(iConsole.Controls.TextBox), Theme())!;
        box.SetFocus();

        Assert.IsTrue(box.GetKeyHints().Any(h => h.Description == "Einfügen"));
    }

    [TestMethod]
    public void TheEnglishPrompt_KeepsContinueOnCAndCancelOnA()
    {
        UiText.Current = null;

        Assert.AreEqual("C|a", HotKeys(new ErrorPromptDialog("t", "m", Theme())));
    }

    [TestMethod]
    public void ATranslatedPrompt_TakesItsHotKeysFromItsOwnWords()
    {
        Speak("de");

        Assert.AreEqual("W|A", HotKeys(new ErrorPromptDialog("Titel", "Nachricht", Theme())));
    }
}
