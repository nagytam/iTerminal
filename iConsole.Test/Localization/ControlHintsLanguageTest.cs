using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Charts;
using iConsole.Localization;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The key hints of the basic controls and the empty texts of the charts and the table follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class ControlHintsLanguageTest
{
    private static readonly string[] Keys =
    [
        "Hint.DismissMessage", "Hint.MoveHighlightTrail", "Hint.FirstOrLastSegment", "Hint.OpenHighlightedSegment", "Hint.PressButton",
        "Hint.MoveSelectedDay", "Hint.PreviousNextMonth", "Hint.FirstLastDayOfMonth", "Hint.SelectToday", "Hint.PreviousNextYear",
        "Hint.ChooseSelectedDay", "Hint.CheckOrUncheck", "Hint.MoveCursor", "Hint.MoveCursorPage", "Hint.FirstOrLastItem", "Hint.ActivateList",
        "Hint.CheckItemUnderCursor", "Hint.CheckEveryItem", "Hint.InvertEveryItem", "Hint.MoveBetweenChips", "Hint.FirstOrLastChip",
        "Hint.AddChip", "Hint.RemoveActiveChip", "Hint.MoveOverPalette", "Hint.FirstOrLastColour", "Hint.ChooseColourUnderCursor",
        "Hint.ChoosePreviousNext", "Hint.ChooseItemPageAway", "Hint.JumpToLetter", "Hint.OpenList", "Hint.MoveOverItems",
        "Hint.RunOrOpenSubmenu", "Hint.OpenSubmenu", "Hint.CloseSubmenu", "Hint.CloseMenu", "Common.NoData", "Common.NoRows",
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new();
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
    public void TheEmptyTexts_FollowTheLanguage_UnlessTheyWereSet()
    {
        UiText.Current = null;
        PieChart pie = new(null!);
        Assert.AreEqual("No data", pie.EmptyText);

        Speak("de");
        Assert.AreEqual("Keine Daten", pie.EmptyText);
        Assert.AreEqual("Keine Zeilen", new LiveTable(Mock.Of<IThemeService>(), Mock.Of<ITimeProvider>()).EmptyText);

        pie.EmptyText = "custom";
        Speak("fr");
        Assert.AreEqual("custom", pie.EmptyText, "an explicit text wins over the language");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InGerman_TheCheckBoxHintIsTranslated()
    {
        Speak("de");
        Assert.IsTrue(new CheckBox(Mock.Of<IThemeService>()).GetKeyHints().Any(h => h.Description == "Ankreuzen oder abwählen"));
    }
}
