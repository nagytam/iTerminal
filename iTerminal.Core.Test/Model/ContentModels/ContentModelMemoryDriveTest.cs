using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Model.ContentModels.Test;

[TestClass]
public class ContentModelMemoryDriveTest
{
    private static ContentModelMemoryDrive CreateModel(MemoryDriveService memoryDriveService)
    {
        var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
        var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
        frequentExecutionGuardService
            .Setup(g => g.Execute(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        return new ContentModelMemoryDrive(
            name: "TestPanel",
            settingsService: settingsService.Object,
            memoryDriveService: memoryDriveService,
            folderHistoryService: new FolderHistoryService(settingsService.Object),
            frequentExecutionGuardService: frequentExecutionGuardService.Object);
    }

    [TestMethod]
    public async Task Refresh_AtRoot_ListsRootEntriesWithoutDotDot()
    {
        var memoryDriveService = new MemoryDriveService();
        await memoryDriveService.WriteFileAsync(new Url("memdrive://root.txt"), new MemoryStream([1]));
        var model = CreateModel(memoryDriveService);

        model.SetUrl(new Url(Container.MEMORY_DRIVE));

        var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
        CollectionAssert.Contains(names, "root.txt");
        CollectionAssert.DoesNotContain(names, "..");
    }

    [TestMethod]
    public async Task Refresh_InSubfolder_ListsOnlyThatFoldersChildrenPlusDotDot()
    {
        var memoryDriveService = new MemoryDriveService();
        await memoryDriveService.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDriveService.WriteFileAsync(new Url("memdrive://a/inside.txt"), new MemoryStream([1]));
        await memoryDriveService.WriteFileAsync(new Url("memdrive://outside.txt"), new MemoryStream([1]));
        var model = CreateModel(memoryDriveService);

        model.SetUrl(new Url("memdrive://a/"));

        var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
        CollectionAssert.Contains(names, "inside.txt");
        CollectionAssert.Contains(names, "..");
        CollectionAssert.DoesNotContain(names, "outside.txt");
    }

    [TestMethod]
    public async Task CreateFolderAsync_ThenSetUrl_ShowsNewSubfolder()
    {
        var memoryDriveService = new MemoryDriveService();
        var model = CreateModel(memoryDriveService);
        model.SetUrl(new Url(Container.MEMORY_DRIVE));

        await model.CreateFolderAsync(new Url("memdrive://newfolder/"));
        model.Refresh();

        var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
        CollectionAssert.Contains(names, "newfolder");
    }
}
