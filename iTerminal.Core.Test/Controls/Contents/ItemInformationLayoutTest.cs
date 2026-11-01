using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Moq;
using Serilog;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// The one-line strip under a file panel: the focused entry's name and, right-aligned, its size (or
/// its file count). The size never used to appear at all — see <see cref="ItemInformation.ComposeLine"/>.
/// </summary>
[TestClass]
public class ItemInformationLayoutTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TheDetailIsRightAligned()
    {
        var line = ItemInformation.ComposeLine("File: readme.txt", "File size: 1 KB", 40);

        Assert.AreEqual(40, line.Length);
        Assert.IsTrue(line.StartsWith("File: readme.txt", StringComparison.Ordinal), line);
        Assert.IsTrue(line.EndsWith("File size: 1 KB", StringComparison.Ordinal), line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PaddedTextsAreTrimmedBeforeLayout()
    {
        // The entry pads both texts to the full width; joined as they came, the size started past
        // the right edge of the strip.
        var line = ItemInformation.ComposeLine("File: a.txt".PadRight(40), "File size: 9 B".PadRight(40), 40);

        Assert.IsTrue(line.EndsWith("File size: 9 B", StringComparison.Ordinal), line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALongNameIsShrunkSoTheSizeStaysVisible()
    {
        var line = ItemInformation.ComposeLine("File: " + new string('n', 80) + ".txt", "File size: 12 MB", 40);

        Assert.AreEqual(40, line.Length);
        Assert.IsTrue(line.EndsWith("File size: 12 MB", StringComparison.Ordinal), line);
        Assert.IsTrue(line.StartsWith("File:", StringComparison.Ordinal), line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutADetailTheNameUsesTheWholeWidth()
    {
        Assert.AreEqual("..".PadRight(10), ItemInformation.ComposeLine("..", string.Empty, 10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADetailWiderThanTheStripWins()
    {
        var line = ItemInformation.ComposeLine("File: x", "Number of files: 123456", 12);

        Assert.AreEqual(12, line.Length);
        StringAssert.StartsWith(line, "Num");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANonPositiveWidthIsEmpty()
    {
        Assert.AreEqual(string.Empty, ItemInformation.ComposeLine("a", "b", 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARealFileEntryShowsItsSize()
    {
        var path = Path.Combine(Path.GetTempPath(), "ItemInfo_" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(path, new string('x', 2048));
        try
        {
            var contentModelService = new Mock<IContentModelService>();
            var sut = new ItemInformation(
                contentModelService.Object,
                new Mock<TaskManager>(MockBehavior.Loose).Object,
                new MemoryCacheService(),
                new TestThemeService(),
                Mock.Of<ILogger>())
            {
                ViewSize = new Size(60, 1),
            };
            var model = new Mock<IContentModel>(MockBehavior.Loose);
            model.Setup(m => m.GetFocusedEntry()).Returns(new FileSystemEntry(path));
            sut.ContentModel = model.Object;
            contentModelService.Raise(s => s.ModelChanged += null, EventArgs.Empty);

            var buffer = new ScreenBuffer(60, 1);
            sut.Draw(buffer);
            var line = buffer.Line(0);

            StringAssert.Contains(line, Path.GetFileName(path));
            StringAssert.Contains(line, "File size:");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
