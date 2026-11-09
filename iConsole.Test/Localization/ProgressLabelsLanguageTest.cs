using iConsole.Controls.Progress;
using iConsole.Localization;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The labels of the progress controls follow the chosen language, and a label a caller sets stays as set.</summary>
[TestClass]
[DoNotParallelize]
public class ProgressLabelsLanguageTest
{
    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    [Timeout(60_000)]
    [TestMethod]
    public void TheTimeAndBarLabelsFollowTheLanguage_UnlessSet()
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage("de"));
        _language = UiText.Scope(localizer);
        IThemeService themeService = Mock.Of<IThemeService>();

        ProgressBar bar = new(themeService);
        ProgressTime time = new(Mock.Of<iConsole.Tasks.BackgroundTask>(), themeService);

        Assert.AreEqual("Fortschritt", bar.Label);
        Assert.AreEqual("Verstrichene Zeit", time.LabelElapsed);
        Assert.AreEqual("Verbleibende Zeit", time.LabelRemaining);
        Assert.AreEqual("Gesamtzeit", time.LabelTotal);

        bar.Label = "Mine";
        Assert.AreEqual("Mine", bar.Label);
    }
}
