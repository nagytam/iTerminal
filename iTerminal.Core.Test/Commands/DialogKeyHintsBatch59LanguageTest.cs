using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs.ToDo;

namespace iTerminal.Core.Test.Commands;

/// <summary>The key hints of the torrent, duplicate finder, calendar, to-do, system settings and remote connection dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogKeyHintsBatch59LanguageTest
{
    private static readonly string[] Keys = ["DialogHint.AddTorrent","DialogHint.PasteLinkOrPath","DialogHint.RemoveKeepFiles","DialogHint.RemoveDeleteFiles","DialogHint.KeepTorrent","DialogHint.ChooseTorrent","DialogHint.AddMagnetOrTorrent","DialogHint.PauseOrResume","DialogHint.RemoveTorrentAsks","DialogHint.OpenSaveFolder","DialogHint.DeleteMarkedFiles","DialogHint.DeleteNothing","DialogHint.MoveOverFiles","DialogHint.JumpFirstLastFile","DialogHint.MarkFileAndMoveOn","DialogHint.MarkAllButOldest","DialogHint.MarkAllButNewest","DialogHint.ClearMarks","DialogHint.DeleteMarkedAsks","DialogHint.ScanAgain","DialogHint.ShowFileInPanel","DialogHint.SaveMeeting","DialogHint.DiscardEntry","DialogHint.DeleteMeeting","DialogHint.KeepMeeting","DialogHint.MoveDayOrWeek","DialogHint.GoToToday","DialogHint.ChooseMeetingOfDay","DialogHint.AddMeeting","DialogHint.EditChosenMeeting","DialogHint.DeleteChosenMeetingAsks","DialogHint.SaveTask","DialogHint.DeleteTask","DialogHint.KeepTask","DialogHint.MoveOverTasks","DialogHint.JumpFirstLastTask","DialogHint.AddTask","DialogHint.EditTask","DialogHint.MarkTaskDoneOrOpen","DialogHint.DeleteTaskAsks","DialogHint.ApplyValue","DialogHint.CancelEntry","DialogHint.SwitchSection","DialogHint.MoveOverSettings","DialogHint.FirstOrLastSetting","DialogHint.ChangeSettingUnderCursor","DialogHint.ReadSettingsAgain","DialogHint.AddVariable","DialogHint.RemoveVariableAsks","DialogHint.SwitchUserMachine","DialogHint.SaveConnection","DialogHint.DeleteConnection","DialogHint.KeepConnection","DialogHint.ChooseConnection","DialogHint.ConnectLentTerminal","DialogHint.AddConnection","DialogHint.EditConnection","DialogHint.DeleteConnectionAsks","DialogHint.BrowseConnection"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(ToDoListDialog).Assembly))]);
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
    public void TheToDoHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Dialog? english = DialogHintsWholeTest.Resolve(typeof(ToDoListDialog), 0) as Dialog;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Add a task"));

        Speak("de");
        Dialog? german = DialogHintsWholeTest.Resolve(typeof(ToDoListDialog), 0) as Dialog;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Eine Aufgabe hinzufügen"));
    }
}
