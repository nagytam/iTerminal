using iConsole.Controls;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The field labels and placeholders of the dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogFieldLabelsLanguageTest
{
    private static readonly string[] Keys = ["Field.Compare","Field.PasteExpectedHash","Field.Run","Field.TypeToFilterCommands","Field.IncludeSubfolders","Field.DeleteTargetLacksSource","Field.EncryptFileNames","Field.ReplaceFilesArchiveHas","Field.Link","Field.HardLink","Field.RelativeTarget","Field.Amount","Field.From","Field.MoveToRecycleBin","Field.RecursivelyDeleteFolder","Field.Image","Field.EachArchiveOwnFolder","Field.OverwriteExisting","Field.AppliesTo","Field.RunsInTerminal","Field.Tags","Field.PathOrUrl","Field.SshUserHost","Field.FileSystem","Field.VolumeLabel","Field.QuickFormat","Field.TypeDriveLetterConfirm","Field.Pattern","Field.FoldersToo","Field.Username","Field.PasswordOrKey","Field.ShareName","Field.SaveConnection","Field.Filter","Field.ProcessNameOrId","Field.ReadOnly","Field.Hidden","Field.System","Field.Archive","Field.ModeOctal","Field.Created","Field.Modified","Field.Accessed","Field.Find","Field.NameOrPattern","Field.Containing","Field.TextInsideFiles","Field.In","Field.FolderToSearch","Field.LargerMb","Field.BlankAny","Field.OlderDays","Field.ReadTextFromImages","Field.EnableQuotes","Field.EnableWeather","Field.Key","Field.MemoryThisProcess","Field.Engine"];

    // Words that are the same in that language as in English.
    private static readonly HashSet<(string Language, string Key)> SameWord =
    [
        ("de", "Field.Link"), ("de", "Field.System"), ("de", "Field.Filter"), ("de", "Field.In"), ("it", "Field.In"),
        ("fr", "Field.Image"), ("fr", "Field.Archive"), ("fr", "Field.ModeOctal"), ("it", "Field.FileSystem"),
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
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
                if (!SameWord.Contains((language.Code, key)))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSearchDialogLabels_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Assert.AreEqual("Containing", Label(typeof(iTerminal.Core.Dialogs.Search.FindFilesDialog), "_textBox"));

        Speak("de");
        Assert.AreEqual("Enthaltend", Label(typeof(iTerminal.Core.Dialogs.Search.FindFilesDialog), "_textBox"));
    }

    private static string Label(Type type, string field)
    {
        object? dialog = DialogHintsWholeTest.Resolve(type, 0);
        Assert.IsNotNull(dialog);
        for (Type? t = dialog.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(dialog) is TextBox box)
            {
                return box.Label;
            }
        }

        Assert.Fail(field);
        return string.Empty;
    }
}
