using System.Resources;
using iConsole.Localization;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// The delete dialog's recycle-bin option, the runtime dependency wizard, the update dialog's skip button and the splitter's hint
/// follow the chosen language. The language is a static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class Batch77LanguageTest
{
    private static readonly string[] Keys =
    [
        "Field.MoveToRecycleBinNotHere", "Field.MoveToTrash", "Button.SkipVersion", "Update.InstallDescription", "Update.SkipDescription",
        "RuntimeDependency.NotInstalled", "RuntimeDependency.UpdateTo", "RuntimeDependency.UpToDate", "RuntimeDependency.Intro",
        "RuntimeDependency.DataFolderNote", "RuntimeDependency.NothingToSetUp", "RuntimeDependency.AllUpToDate", "RuntimeDependency.NeedWork",
        "RuntimeDependency.EnterCloses", "RuntimeDependency.NothingTicked", "RuntimeDependency.FinishWillInstall",
        "RuntimeDependency.NothingTickedGoBack", "RuntimeDependency.Footer.Installing", "RuntimeDependency.Footer.InstallWithTick",
        "RuntimeDependency.Footer.InstallOnly", "RuntimeDependency.Footer.NextWithTick", "RuntimeDependency.Footer.NextOnly",
        "RuntimeDependency.Footer.Close", "RuntimeDependency.Tick.NothingToDo", "RuntimeDependency.Tick.Install", "RuntimeDependency.Tick.LeftOut",
        "Hint.MoveDivider",
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(DeleteDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryKey()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                Assert.AreNotEqual(englishText, text, language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRecycleBinOption_StaysInTheChosenLanguage()
    {
        Speak("de");
        Assert.AreEqual("In den Papierkorb verschieben", DeleteDialog.RecycleBinLabel);
        StringAssert.Contains(DeleteDialog.NoRecycleBinLabel, "nicht verfügbar");
        Assert.AreEqual("In den Papierkorb verschieben", DeleteDialog.TrashLabel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheWizardStateText_FollowsTheLanguage()
    {
        var missing = new RuntimeDependencyStatus("playwright", "Playwright", "purpose", null, "1.63.0", RuntimeDependencyState.Missing);

        Speak("en");
        Assert.AreEqual("Not installed. Install 1.63.0.", RuntimeDependencyStepPage.StateText(missing));

        Speak("de");
        Assert.AreEqual("Nicht installiert. 1.63.0 installieren.", RuntimeDependencyStepPage.StateText(missing));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSplitterHint_FollowsTheLanguage()
    {
        Speak("de");
        Assert.IsTrue(UiText.Current!.TryGet("Hint.MoveDivider", out string text));
        Assert.AreEqual("Trennlinie verschieben", text);
    }
}
