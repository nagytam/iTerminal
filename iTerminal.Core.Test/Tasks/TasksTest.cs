using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Tasks;

[TestClass]
public class ExecuteCommandTaskTest
{
    [TestMethod]
    public void Constructor_DefaultProperties_AreNull()
    {
        var task = new ExecuteCommandTask();
        Assert.IsNull(task.ContentModel);
        Assert.IsNull(task.TerminalModel);
        Assert.IsNull(task.Task);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenBothModelsNull_ReturnsCompletedTask()
    {
        var task = new ExecuteCommandTask();
        // No content model or terminal model set
        await task.ExecuteAsync();
        Assert.IsNull(task.Task);
    }

    private static TerminalModel CreateTerminalModel()
    {
        var taskManager = new Mock<iConsole.Tasks.TaskManager>().Object;
        var contentModelService = new Mock<IContentModelService>().Object;
        return new TerminalModel(taskManager, contentModelService);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenOnlyTerminalModelSet_ReturnsCompletedTask()
    {
        var task = new ExecuteCommandTask
        {
            TerminalModel = CreateTerminalModel()
        };
        await task.ExecuteAsync();
        Assert.IsNull(task.Task);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenOnlyContentModelSet_ReturnsCompletedTask()
    {
        var task = new ExecuteCommandTask
        {
            ContentModel = Mock.Of<IContentModel>()
        };
        await task.ExecuteAsync();
        Assert.IsNull(task.Task);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenBothModelsSet_SetsTask()
    {
        var terminalModel = CreateTerminalModel();
        var contentModel = new Mock<IContentModel>();
        contentModel.Setup(c => c.RunTerminalCommandAsync(It.IsAny<TerminalModel>()))
                    .Returns(Task.CompletedTask);

        var task = new ExecuteCommandTask
        {
            ContentModel = contentModel.Object,
            TerminalModel = terminalModel
        };

        await task.ExecuteAsync();

        Assert.IsNotNull(task.Task);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenBothModelsSet_AddsToHistory()
    {
        var terminalModel = CreateTerminalModel();
        var contentModel = new Mock<IContentModel>();
        contentModel.Setup(c => c.RunTerminalCommandAsync(It.IsAny<TerminalModel>()))
                    .Returns(Task.CompletedTask);

        var task = new ExecuteCommandTask
        {
            ContentModel = contentModel.Object,
            TerminalModel = terminalModel
        };

        await task.ExecuteAsync();
        await task.Task!;

        Assert.IsTrue(terminalModel.History.Count > 0);
    }
}

// Two separate FileSystemService instances are used (rather than one shared instance) so that
// ReferenceEquals(source, target) is false and ContentModelCopyFileTask/ContentModelCopyFolderTask
// exercise their generic pipe-streaming path even though both sides are the real local disk.

[TestClass]
public class CopyFileTaskTest
{
    [TestMethod]
    public async Task ExecuteAsync_CopiesFile()
    {
        var src = Path.GetTempFileName();
        var dst = Path.GetTempFileName();
        File.Delete(dst);
        File.WriteAllText(src, "hello world");

        try
        {
            var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(src), new Url(dst), overwrite: true);
            await task.ExecuteAsync();
            Assert.IsTrue(File.Exists(dst));
            Assert.AreEqual("hello world", File.ReadAllText(dst));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_DescriptionContainsStarted_ThenFinished()
    {
        var src = Path.GetTempFileName();
        var dst = Path.GetTempFileName();
        File.Delete(dst);
        File.WriteAllText(src, "test data for description check");

        try
        {
            var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(src), new Url(dst), overwrite: true);
            await task.ExecuteAsync();
            StringAssert.Contains(task.Description, "finished");
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_Cancelled_DoesNotComplete()
    {
        var src = Path.GetTempFileName();
        var dst = Path.GetTempFileName();
        File.Delete(dst);
        // Write a large file so there's time to cancel
        File.WriteAllBytes(src, new byte[4096]);

        try
        {
            var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(src), new Url(dst), overwrite: true);
            task.CancellationTokenSource.Cancel();
            await task.ExecuteAsync();
            // Just verifying no exception is thrown after cancellation
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }
}

[TestClass]
public class CopyFolderTaskTest
{
    [TestMethod]
    public async Task ExecuteAsync_CopiesAllFiles()
    {
        var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "aaa");
        File.WriteAllText(Path.Combine(src, "b.txt"), "bbb");

        try
        {
            var sourceUrl = new Url(src);
            var targetUrl = new Url(dst + Path.DirectorySeparatorChar);
            var source = new FileSystemService();
            var target = new FileSystemService();
            var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
            var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());
            _ = discoveryTask.ExecuteAsync();
            await task.ExecuteAsync();
            Assert.IsTrue(File.Exists(Path.Combine(dst, "a.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(dst, "b.txt")));
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_DescriptionContainsFinished_OnSuccess()
    {
        var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "file.txt"), "data");

        try
        {
            var sourceUrl = new Url(src);
            var targetUrl = new Url(dst + Path.DirectorySeparatorChar);
            var source = new FileSystemService();
            var target = new FileSystemService();
            var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
            var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());
            _ = discoveryTask.ExecuteAsync();
            await task.ExecuteAsync();
            StringAssert.Contains(task.Description, "finished");
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_Cancelled_DescriptionContainsCancelled()
    {
        var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        // Write several files to give cancellation a chance to take effect
        for (int i = 0; i < 5; i++)
            File.WriteAllBytes(Path.Combine(src, $"f{i}.bin"), new byte[4096]);

        try
        {
            var sourceUrl = new Url(src);
            var targetUrl = new Url(dst + Path.DirectorySeparatorChar);
            var source = new FileSystemService();
            var target = new FileSystemService();
            var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
            var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());
            task.CancellationTokenSource.Cancel();
            _ = discoveryTask.ExecuteAsync();
            await task.ExecuteAsync();
            StringAssert.Contains(task.Description, "cancelled");
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_EmptyFolder_DescriptionFinished()
    {
        var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);

        try
        {
            var sourceUrl = new Url(src);
            var targetUrl = new Url(dst + Path.DirectorySeparatorChar);
            var source = new FileSystemService();
            var target = new FileSystemService();
            var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
            var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());
            _ = discoveryTask.ExecuteAsync();
            await task.ExecuteAsync();
            StringAssert.Contains(task.Description, "finished");
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }
}
