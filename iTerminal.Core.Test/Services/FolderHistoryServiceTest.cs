using iConsole.Data;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class FolderHistoryServiceTest
{
    [TestMethod]
    public void Constructor_InitializesHistory()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);

        var service = new FolderHistoryService(mockSettingsService.Object);

        Assert.IsNotNull(service.History);
        Assert.AreEqual(20, service.MaxHistoryItems);
    }

    [TestMethod]
    public void Constructor_LoadsHistoryFromSettings()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(SettingsService.FOLDER_HISTORY))
            .Returns("file:///path1;file:///path2;file:///path3");

        var service = new FolderHistoryService(mockSettingsService.Object);

        Assert.AreEqual(3, service.History.Count);
        Assert.IsTrue(service.History.Contains("file:///path1"));
        Assert.IsTrue(service.History.Contains("file:///path2"));
        Assert.IsTrue(service.History.Contains("file:///path3"));
    }

    [TestMethod]
    public void Add_AddsNewUrl()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);
        var url = new Url("file:///test/path");

        service.Add(url);

        Assert.AreEqual(1, service.History.Count);
        Assert.IsTrue(service.History.Contains(url.FullUrl));
    }

    [TestMethod]
    public void Add_InsertsAtBeginning()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);
        var url1 = new Url("file:///path1");
        var url2 = new Url("file:///path2");

        service.Add(url1);
        service.Add(url2);

        Assert.AreEqual(url2.FullUrl, service.History[0]);
        Assert.AreEqual(url1.FullUrl, service.History[1]);
    }

    [TestMethod]
    public void Add_DoesNotAddDuplicates()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);
        var url = new Url("file:///same/path");

        service.Add(url);
        service.Add(url);

        Assert.AreEqual(1, service.History.Count);
    }

    [TestMethod]
    public void Add_RemovedOldestWhenMaxReached()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);
        service.MaxHistoryItems = 3;

        service.Add(new Url("file:///path1"));
        service.Add(new Url("file:///path2"));
        service.Add(new Url("file:///path3"));
        service.Add(new Url("file:///path4"));

        Assert.AreEqual(3, service.History.Count);
        Assert.IsFalse(service.History.Contains("file:///path1"));
        Assert.IsTrue(service.History.Contains("file:///path4"));
    }

    [TestMethod]
    public void Add_UpdatesSettings()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);
        var url = new Url("file:///test");

        service.Add(url);

        mockSettingsService.Verify(s => s.Set(SettingsService.FOLDER_HISTORY, It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public void MaxHistoryItems_CanBeModified()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);

        service.MaxHistoryItems = 50;

        Assert.AreEqual(50, service.MaxHistoryItems);
    }

    [TestMethod]
    public void Add_MultipleUrls_MaintainsOrder()
    {
        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var service = new FolderHistoryService(mockSettingsService.Object);

        service.Add(new Url("file:///path1"));
        service.Add(new Url("file:///path2"));
        service.Add(new Url("file:///path3"));

        Assert.AreEqual("file:///path3", service.History[0]);
        Assert.AreEqual("file:///path2", service.History[1]);
        Assert.AreEqual("file:///path1", service.History[2]);
    }
}
