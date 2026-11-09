using iTerminal.Core.Services.Properties;

namespace iTerminal.Core.Test.Services.Properties;

[TestClass]
public class FilePropertiesServiceTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "FilePropertiesServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetTimes_WritesTheModifiedAndAccessedDatesOfAFile()
    {
        var path = Path.Combine(_folder, "a.txt");
        File.WriteAllText(path, "x");
        var when = new DateTime(2021, 6, 7, 8, 9, 10);

        new FilePropertiesService().SetTimes(path, created: null, modified: when, accessed: when.AddHours(1));

        FileProperties read = new FilePropertiesService().Read(path);
        Assert.AreEqual(when, read.Modified);
        Assert.AreEqual(when.AddHours(1), read.Accessed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetTimes_WritesTheModifiedDateOfAFolder()
    {
        var when = new DateTime(2019, 1, 2, 3, 4, 5);

        new FilePropertiesService().SetTimes(_folder, created: null, modified: when, accessed: null);

        Assert.AreEqual(when, new FilePropertiesService().Read(_folder).Modified);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetTimes_WithNothingToSet_TouchesNothing()
    {
        var path = Path.Combine(_folder, "b.txt");
        File.WriteAllText(path, "x");
        var before = File.GetLastWriteTime(path);

        new FilePropertiesService().SetTimes(path, created: null, modified: null, accessed: null);

        Assert.AreEqual(before, File.GetLastWriteTime(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DoNotParallelize]
    public void TheByteCountFollowsTheChosenLanguage()
    {
        iConsole.Localization.Localizer localizer = new([new iConsole.Localization.ResourceLocalizationSource(
            new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(FilePropertiesService).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage("de"));
        using IDisposable language = iConsole.Localization.UiText.Scope(localizer);

        StringAssert.EndsWith(FilePropertiesService.FormatSize(40), "(40 Bytes)");
        StringAssert.EndsWith(FilePropertiesService.FormatSize(1), "(1 Byte)");
    }
}
