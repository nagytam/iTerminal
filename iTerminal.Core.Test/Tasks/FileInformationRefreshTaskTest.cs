using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Tasks.Test;

/// <summary>The folder size shown in a panel: the real count of what is inside, or "?" when some of it could not be read.</summary>
[TestClass]
public class FileInformationRefreshTaskTest
{
    /// <summary>Exposes the progress handler, so a test can deliver a report the way <c>Progress</c> does, after the count.</summary>
    private sealed class CountingTask(IEntry entry, IContentModel model) : FileInformationRefreshTask(entry, model, Mock.Of<IMemoryCacheService>(), Log.Logger)
    {
        public void DeliverLateProgress(long value) => OnProgressChanged(value);
    }

    private static Mock<IEntry> FolderEntry(string path)
    {
        var entry = new Mock<IEntry>();
        var url = new Url(path + Path.DirectorySeparatorChar);
        entry.SetupGet(e => e.Url).Returns(url);
        entry.SetupGet(e => e.FullName).Returns(path);
        entry.SetupProperty(e => e.NumberOfFiles);
        return entry;
    }

    private static async Task CountAsync(IEntry entry)
    {
        var task = new FileInformationRefreshTask(entry, Mock.Of<IContentModel>(), Mock.Of<IMemoryCacheService>(), Log.Logger);
        await task.ExecuteAsync();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderThatCannotBeRead_ShowsAQuestionMark_NotAShortCount()
    {
        var missing = Path.Combine(Path.GetTempPath(), "NoSuchFolder_" + Guid.NewGuid().ToString("N"));
        var entry = FolderEntry(missing);

        await CountAsync(entry.Object);

        Assert.AreEqual("?", entry.Object.NumberOfFiles);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReadableFolder_ShowsItsCount()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CountTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
            File.WriteAllText(Path.Combine(folder, "b.txt"), "b");
            File.WriteAllText(Path.Combine(folder, "sub", "c.txt"), "c");
            var entry = FolderEntry(folder);

            await CountAsync(entry.Object);

            Assert.AreEqual("3", entry.Object.NumberOfFiles);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AProgressReportDeliveredAfterTheCount_DoesNotPutTheEllipsisBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "LateProgress_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
            var entry = FolderEntry(folder);
            var task = new CountingTask(entry.Object, Mock.Of<IContentModel>());
            await task.ExecuteAsync();
            Assert.AreEqual("1", entry.Object.NumberOfFiles);

            task.DeliverLateProgress(1);

            Assert.AreEqual("1", entry.Object.NumberOfFiles, "the final count stays");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
