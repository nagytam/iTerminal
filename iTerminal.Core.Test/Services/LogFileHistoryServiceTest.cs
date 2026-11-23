using iConsole.Data;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Services;

/// <summary>The F3 log history: the newest logs are kept up to the configured size, and Older and Newer step between the ones that still exist.</summary>
[TestClass]
public class LogFileHistoryServiceTest
{
    private readonly List<string> _files = [];
    private readonly Dictionary<string, string?> _settings = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string file in _files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private LogFileHistoryService NewService()
    {
        Mock<ISettingsService> settings = new();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string key) => _settings.GetValueOrDefault(key));
        return new LogFileHistoryService(settings.Object);
    }

    private string TempLog(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), $"log-history-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, name);
        _files.Add(path);
        return path;
    }

    private static Url FileUrl(string path) => new(Container.FILE + path.Replace('\\', '/'));

    [TestMethod]
    public void Capacity_IsTen_WhenTheSettingIsMissing_OrNotANumber()
    {
        Assert.AreEqual(10, NewService().Capacity);

        _settings[LogFileHistoryService.SizeSettingKey] = "many";
        Assert.AreEqual(10, NewService().Capacity);
    }

    [TestMethod]
    public void Capacity_IsTheSetting_ClampedToOneAndOneHundred()
    {
        _settings[LogFileHistoryService.SizeSettingKey] = "4";
        Assert.AreEqual(4, NewService().Capacity);

        _settings[LogFileHistoryService.SizeSettingKey] = "0";
        Assert.AreEqual(1, NewService().Capacity);

        _settings[LogFileHistoryService.SizeSettingKey] = "500";
        Assert.AreEqual(100, NewService().Capacity);
    }

    [TestMethod]
    public void Record_KeepsOnlyTheSettingsNumberOfLogs_DroppingTheOldest()
    {
        _settings[LogFileHistoryService.SizeSettingKey] = "2";
        LogFileHistoryService history = NewService();
        string a = TempLog("a.log");
        string b = TempLog("b.log");
        string c = TempLog("c.log");

        history.Record(FileUrl(a));
        history.Record(FileUrl(b));
        history.Record(FileUrl(c));

        Assert.IsNull(history.Older(FileUrl(b)), "b is the oldest kept, so nothing is older");
        Assert.AreEqual(FileUrl(b).FullUrl, history.Older(FileUrl(c))!.FullUrl);
        Assert.IsNull(history.Older(FileUrl(a)), "a was dropped, so it is not in the history at all");
    }

    [TestMethod]
    public void Record_OfAnExistingLog_MovesItToTheNewest()
    {
        LogFileHistoryService history = NewService();
        string a = TempLog("a.log");
        string b = TempLog("b.log");
        history.Record(FileUrl(a));
        history.Record(FileUrl(b));

        history.Record(FileUrl(a));

        Assert.AreEqual(FileUrl(a).FullUrl, history.Newer(FileUrl(b))!.FullUrl, "a is the newest again");
        Assert.AreEqual(FileUrl(b).FullUrl, history.Older(FileUrl(a))!.FullUrl, "and b, the log before it, is the older one");
    }

    [TestMethod]
    public void OlderAndNewer_StepAcrossTheHistory_ThenStop()
    {
        LogFileHistoryService history = NewService();
        string a = TempLog("a.log");
        string b = TempLog("b.log");
        string c = TempLog("c.log");
        history.Record(FileUrl(a));
        history.Record(FileUrl(b));
        history.Record(FileUrl(c));

        Assert.AreEqual(FileUrl(b).FullUrl, history.Older(FileUrl(c))!.FullUrl);
        Assert.AreEqual(FileUrl(a).FullUrl, history.Older(FileUrl(b))!.FullUrl);
        Assert.IsNull(history.Older(FileUrl(a)));
        Assert.AreEqual(FileUrl(c).FullUrl, history.Newer(FileUrl(b))!.FullUrl);
        Assert.IsNull(history.Newer(FileUrl(c)));
    }

    [TestMethod]
    public void OlderAndNewer_SkipAFileThatNoLongerExists()
    {
        LogFileHistoryService history = NewService();
        string a = TempLog("a.log");
        string b = TempLog("b.log");
        string c = TempLog("c.log");
        history.Record(FileUrl(a));
        history.Record(FileUrl(b));
        history.Record(FileUrl(c));
        File.Delete(b);

        Assert.AreEqual(FileUrl(a).FullUrl, history.Older(FileUrl(c))!.FullUrl);
    }

    [TestMethod]
    public void Record_IgnoresARemoteFile()
    {
        LogFileHistoryService history = NewService();
        string a = TempLog("a.log");
        history.Record(new Url("ssh://host/var/log/remote.log"));
        history.Record(FileUrl(a));

        Assert.IsNull(history.Older(FileUrl(a)));
    }

    [TestMethod]
    public void OlderAndNewer_ForALogNotInTheHistory_AreNull()
    {
        LogFileHistoryService history = NewService();

        Assert.IsNull(history.Older(FileUrl(TempLog("unknown.log"))));
        Assert.IsNull(history.Newer(FileUrl(TempLog("unknown2.log"))));
    }
}
