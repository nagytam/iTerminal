using System.Reflection;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Wizard;
using iConsole.Data;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The wizard's Back, Next and Finish buttons speak the chosen language. The language is a static, so these run on their own.</summary>
[TestClass]
[DoNotParallelize]
public class WizardLanguageTest
{
    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static void Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
    }

    private static Wizard Create()
    {
        IThemeService theme = Theme();
        Wizard wizard = new(theme) { ViewSize = new Size(40, 8) };
        wizard.AddStep(new WizardStep("One", new Label("page", theme)));
        wizard.AddStep(new WizardStep("Two", new Label("page", theme)));
        return wizard;
    }

    private static string ButtonTitle(Wizard wizard, string field) =>
        ((Button)typeof(Wizard).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wizard)!).Title;

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheButtons_FollowTheChosenLanguageAsThePagesChange()
    {
        Speak("de");
        Wizard wizard = Create();

        Assert.AreEqual("Zurück", ButtonTitle(wizard, "_backButton"));
        Assert.AreEqual("Weiter", ButtonTitle(wizard, "_nextButton"));

        await wizard.NextAsync();

        Assert.AreEqual("Fertigstellen", ButtonTitle(wizard, "_nextButton"), "the last page offers Finish");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheButtons_AreEnglishWithNoLanguageChosen()
    {
        Wizard wizard = Create();

        Assert.AreEqual("Back", ButtonTitle(wizard, "_backButton"));
        Assert.AreEqual("Next", ButtonTitle(wizard, "_nextButton"));

        await wizard.NextAsync();

        Assert.AreEqual("Finish", ButtonTitle(wizard, "_nextButton"));
    }
}
