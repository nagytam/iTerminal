using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class CalculateFolderSizeCommandTest
{
    [TestMethod]
    [Timeout(30000)]
    public async Task CtrlSpace_PutsTheFolderTotalInTheSizeColumn()
    {
        var root = Path.Combine(Path.GetTempPath(), "iterminal-foldersize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[2000]);
            var entry = new FileSystemEntry(root + Path.DirectorySeparatorChar);
            var model = new Mock<IContentModel>();
            model.Setup(m => m.GetFocusedEntry()).Returns(entry);
            var service = new Mock<IContentModelService>();
            service.Setup(s => s.GetFocusedModel()).Returns(model.Object);
            var command = new CalculateFolderSizeCommand(service.Object, model.Object);

            Assert.IsTrue(command.CanExecute());
            await command.ExecuteAsync(null);
            await command.PendingOperation;

            Assert.AreNotEqual("…", entry.FileSize);
            Assert.IsTrue(entry.FileSize.Contains("2.9") || entry.FileSize.Contains("3000") || entry.FileSize.Contains("KB", StringComparison.OrdinalIgnoreCase),
                $"unexpected size text '{entry.FileSize}'");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingToMeasureOnAFile()
    {
        var file = Path.GetTempFileName();
        try
        {
            var model = new Mock<IContentModel>();
            model.Setup(m => m.GetFocusedEntry()).Returns(new FileSystemEntry(file));
            var service = new Mock<IContentModelService>();
            service.Setup(s => s.GetFocusedModel()).Returns(model.Object);

            Assert.IsFalse(new CalculateFolderSizeCommand(service.Object, model.Object).CanExecute());
        }
        finally
        {
            File.Delete(file);
        }
    }
}
