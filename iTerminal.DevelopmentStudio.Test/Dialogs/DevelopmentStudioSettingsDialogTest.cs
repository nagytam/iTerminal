using iConsole.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.DevelopmentStudio.Test.Dialogs;

[TestClass]
public class DevelopmentStudioSettingsDialogTest
{
    private static readonly DotnetSdk Sdk10 = new("10.0.100", "/sdk/10");
    private static readonly DotnetSdk Sdk9 = new("9.0.100", "/sdk/9");
    private static readonly IReadOnlyList<DotnetSdk> Sdks = [Sdk10, Sdk9];

    [TestMethod]
    public void TheGeneralPageSavesTheTabsItIsSetTo()
    {
        var settings = new FakeSettingsService();
        var page = new StudioGeneralSettingsTabPage(settings, TestTheme());
        page.Load();

        page.Explorer = DevelopmentStudioDialog.ExplorerMode.Tests;
        page.Output = DevelopmentStudioDialog.OutputTab.Profile;

        Assert.AreEqual("Tests", settings.Get(StudioSettings.ExplorerModeKey));
        Assert.AreEqual("Profile", settings.Get(StudioSettings.OutputTabKey));
    }

    [TestMethod]
    public void TheGeneralPageShowsTheSavedTabsAndSavesNothingWhileLoading()
    {
        var settings = new FakeSettingsService();
        settings.Set(StudioSettings.ExplorerModeKey, "Files");
        settings.Set(StudioSettings.OutputTabKey, "Debug");
        var page = new StudioGeneralSettingsTabPage(settings, TestTheme());

        page.Load();

        Assert.AreEqual(DevelopmentStudioDialog.ExplorerMode.Files, page.Explorer);
        Assert.AreEqual(DevelopmentStudioDialog.OutputTab.Debug, page.Output);
        Assert.AreEqual("Files", settings.Get(StudioSettings.ExplorerModeKey), "filling the page is not a change");
    }

    [TestMethod]
    public void TheBuildPageSavesTheSdkAndTheSbomFormat()
    {
        var settings = new FakeSettingsService();
        var page = new StudioBuildSettingsTabPage(settings, Sdks, TestTheme());
        page.Load();

        page.Sdk = Sdk9;
        page.Sbom = SbomFormat.Json;

        Assert.AreEqual(Sdk9.HostPath, settings.Get(StudioSettings.SdkHostKey));
        Assert.AreEqual("Json", settings.Get(StudioSettings.SbomFormatKey));
    }

    [TestMethod]
    public void TheBuildPageShowsTheSavedSdkAndFallsBackToTheDefault()
    {
        var settings = new FakeSettingsService();
        settings.Set(StudioSettings.SdkHostKey, Sdk9.HostPath);
        var page = new StudioBuildSettingsTabPage(settings, Sdks, TestTheme());

        page.Load();
        Assert.AreEqual(Sdk9, page.Sdk);
        Assert.AreEqual(SbomFormat.Xml, page.Sbom, "a missing SBOM format is the XML one");

        settings.Set(StudioSettings.SdkHostKey, "/sdk/gone");
        page.Load();
        Assert.IsNotNull(page.Sdk, "a saved SDK that is no longer installed gives the default one");
    }

    [TestMethod]
    public void TheLayoutPageRestoresTheDefaultArrangement()
    {
        var settings = new FakeSettingsService();
        settings.Set(StudioSettings.LayoutKey, "something else");
        var page = new StudioLayoutSettingsTabPage(settings, TestTheme());

        page.RestoreDefaultLayout();

        Assert.AreEqual(StudioPanelLayout.Default().Serialize(), settings.Get(StudioSettings.LayoutKey));
    }

    [TestMethod]
    public void TheAnalysisPageForgetsTheTimeEstimates()
    {
        var settings = new FakeSettingsService();
        settings.Set(StudioSettings.AnalysisDurationsKey, StudioSettings.EncodeDurations(new Dictionary<string, double> { ["/src/App.slnx"] = 2.5 }));
        var page = new StudioAnalysisSettingsTabPage(settings, TestTheme());

        page.ForgetDurations();

        Assert.AreEqual(0, StudioSettings.DecodeDurations(settings.Get(StudioSettings.AnalysisDurationsKey)).Count);
    }

    [TestMethod]
    public async Task TheDialogHasOneTabPerCategoryAndTellsTheStudioWhenItCloses()
    {
        var settings = new FakeSettingsService();
        bool closed = false;
        var dialog = new DevelopmentStudioSettingsDialog(settings, Sdks, TestTheme()) { Closed = () => closed = true };
        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "1-General");
        AssertScreenContains(screen, "2-Build");
        AssertScreenContains(screen, "3-Layout");
        AssertScreenContains(screen, "4-Analysis");

        dialog.Close();
        Assert.IsTrue(closed, "the studio reads the saved choices when the dialog closes");
    }
}
