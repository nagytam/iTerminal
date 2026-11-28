using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class StudioSettingsTest
{
    [TestMethod]
    public void AListReadsBackAsItWasWritten()
    {
        string[] folders = ["/src/App.slnx", "/src/App/Properties", "C:\\Work\\Scripts \"quoted\""];

        CollectionAssert.AreEqual(folders, StudioSettings.DecodeList(StudioSettings.EncodeList(folders)).ToArray());
    }

    [TestMethod]
    public void AMissingOrDamagedListIsEmpty()
    {
        Assert.AreEqual(0, StudioSettings.DecodeList(null).Count);
        Assert.AreEqual(0, StudioSettings.DecodeList("").Count);
        Assert.AreEqual(0, StudioSettings.DecodeList("{not json").Count);
        Assert.AreEqual(0, StudioSettings.DecodeList("{\"a\":1}").Count);
    }

    [TestMethod]
    public void TheAnalysisDurationsReadBackAsTheyWereWritten()
    {
        var durations = new Dictionary<string, double> { ["/src/App.slnx"] = 12.5 };

        Assert.AreEqual(12.5, StudioSettings.DecodeDurations(StudioSettings.EncodeDurations(durations))["/src/App.slnx"]);
        Assert.AreEqual(0, StudioSettings.DecodeDurations(null).Count);
        Assert.AreEqual(0, StudioSettings.DecodeDurations("{not json").Count);
    }

    [TestMethod]
    public void AnEmptyEntryInTheListIsLeftOut()
    {
        CollectionAssert.AreEqual(new[] { "/src/App" }, StudioSettings.DecodeList("[\"\",null,\"/src/App\"]").ToArray());
    }

    [TestMethod]
    public void TheKeysAreNotReservedForTheSettingsService()
    {
        foreach (string key in new[]
        {
            StudioSettings.LayoutKey,
            StudioSettings.SbomFormatKey,
            StudioSettings.SdkHostKey,
            StudioSettings.SdkVersionKey,
            StudioSettings.ExplorerModeKey,
            StudioSettings.OutputTabKey,
            StudioSettings.ExpandedFoldersKey,
            StudioSettings.SelectedPathKey,
        })
        {
            Assert.IsFalse(key.StartsWith("__", StringComparison.Ordinal), $"{key} must not be reserved");
        }
    }
}
