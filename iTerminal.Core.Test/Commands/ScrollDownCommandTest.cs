using iConsole.Commands;
using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class ScrollDownCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteDownAtLastPosition()
    {
        var model = CreateModel(0, 0, 1, 10);
        model.Entries.Add(new FileSystemEntry("..."));
        var command = new ScrollDownCommand(model);
        Assert.AreEqual(0, model.ScrollPosition.Y);
        Assert.AreEqual(0, model.CursorPosition.Y);
        await command.ExecuteAsync(null);
        Assert.AreEqual(0, model.ScrollPosition.Y);
        Assert.AreEqual(0, model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteDownWithoutScrolling()
    {
        var model = CreateModel(0, 0, 2, 10);
        model.Entries.Add(new FileSystemEntry(".."));
        model.Entries.Add(new FileSystemEntry("1"));
        var command = new ScrollDownCommand(model);
        Assert.AreEqual(0, model.ScrollPosition.Y);
        Assert.AreEqual(0, model.CursorPosition.Y);
        await command.ExecuteAsync(null);
        Assert.AreEqual(0, model.ScrollPosition.Y);
        Assert.AreEqual(1, model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteDownWithScrolling()
    {
        var model = CreateModel(0, 0, 2, 5);
        model.Entries.Add(new FileSystemEntry(".."));
        model.Entries.Add(new FileSystemEntry("1"));
        var command = new ScrollDownCommand(model);
        Assert.AreEqual(0, model.ScrollPosition.Y);
        Assert.AreEqual(0, model.CursorPosition.Y);
        await command.ExecuteAsync(null);
        Assert.AreEqual(0, model.ScrollPosition.Y);
        Assert.AreEqual(1, model.CursorPosition.Y);
    }

    private static ContentModelFileSystem CreateModel(int scrollY, int cursorIndex, int scrollableSize, int viewSize)
    {
        var mockTaskManager = new Mock<TaskManager>();
        var mockSettingsService = new Mock<SettingsService>();
        var mockFolderHistoryService = new Mock<FolderHistoryService>(mockSettingsService.Object);
        var mockFileSystemService = new Mock<FileSystemService>();
        var mockMemoryCacheService = new Mock<IMemoryCacheService>();
        var mockLogger = new Mock<Serilog.ILogger>();
        var model = new ContentModelFileSystem(
            "contentModel", 
            mockTaskManager.Object, 
            mockSettingsService.Object,
            mockFolderHistoryService.Object,
            mockFileSystemService.Object,
            new FrequentExecutionGuardService(new DateTimeProviderService()),
            mockMemoryCacheService.Object,
            mockLogger.Object
            )
        {
            ScrollPosition = new(0, scrollY),
            CursorPosition = new(0, cursorIndex),
            ScrollableSize = new Size(1, scrollableSize),
            ViewSize = new Size(1, viewSize)
        };
        return model;
    }
}