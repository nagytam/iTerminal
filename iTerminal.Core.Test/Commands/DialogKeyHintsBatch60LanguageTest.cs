using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The key hints of the solution viewer, password manager, open-with, map, workspace and games dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogKeyHintsBatch60LanguageTest
{
    private static readonly string[] Keys = ["DialogHint.SelectProject","DialogHint.SelectAllProjects","DialogHint.SelectAllTestProjects","DialogHint.BuildSelected","DialogHint.CleanSelected","DialogHint.RunTests","DialogHint.RunProject","DialogHint.CancelRunningCommand","DialogHint.NextDotnetSdk","DialogHint.ScrollOutput","DialogHint.TopOfOutputOrFollow","DialogHint.ShowHidePasswords","DialogHint.RemoveProgramFromList","DialogHint.KeepProgram","DialogHint.ChooseProgram","DialogHint.JumpFirstLastProgram","DialogHint.OpenWithChosenProgram","DialogHint.AddProgram","DialogHint.ChangeChosenProgram","DialogHint.RemoveChosenProgramAsks","DialogHint.MakeProgramDefault","DialogHint.BackToMap","DialogHint.PanMap","DialogHint.ZoomInOut","DialogHint.BackToHomeView","DialogHint.SearchPlace","DialogHint.OpenSavedPlaces","DialogHint.SaveCentreAsPlace","DialogHint.ReloadTiles","DialogHint.GoOnlineOffline","DialogHint.SaveAreaOffline","DialogHint.SaveWorkspaceUnderName","DialogHint.ChooseWorkspace","DialogHint.SwitchToWorkspace","DialogHint.SwitchWorkspaceByNumber","DialogHint.SaveScreenAsWorkspace","DialogHint.UpdateWorkspace","DialogHint.RenameWorkspace","DialogHint.DeleteWorkspaceAsks","DialogHint.PlaySelectTake","DialogHint.SameAsEnter","DialogHint.PreviousNextGame","DialogHint.ChooseGame","DialogHint.StartGameAgain","DialogHint.TakeBackMove","DialogHint.SwitchComputerTwoPlayers"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(OpenWithDialog).Assembly))]);
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
    public void TheOpenWithHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Dialog? english = DialogHintsWholeTest.Resolve(typeof(OpenWithDialog), 0) as Dialog;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Add a program"));

        Speak("de");
        Dialog? german = DialogHintsWholeTest.Resolve(typeof(OpenWithDialog), 0) as Dialog;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Ein Programm hinzufügen"));
    }
}
