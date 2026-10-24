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

    [TestMethod]
    public async Task AFolderThatCannotBeRead_ShowsAQuestionMark_NotAShortCount()
    {
        var missing = Path.Combine(Path.GetTempPath(), "NoSuchFolder_" + Guid.NewGuid().ToString("N"));
        var entry = FolderEntry(missing);

        await CountAsync(entry.Object);

        Assert.AreEqual("?", entry.Object.NumberOfFiles);
    }

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
}
