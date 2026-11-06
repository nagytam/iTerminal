using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Security;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Services.Security;

[TestClass]
public class FileDisguiseCheckerTest
{
    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "disguise-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DetectAsync_ReadsTheFirstBytesAndCachesTheKind()
    {
        string folder = NewFolder();
        try
        {
            string path = Path.Combine(folder, "report.pdf");
            await File.WriteAllBytesAsync(path, [0x4D, 0x5A, 0x90, 0x00, 0x03]);

            FileKind kind = await new FileDisguiseChecker().DetectAsync(path);

            Assert.AreEqual(FileKind.Executable, kind);
            Assert.IsTrue(FileDisguiseCache.TryGet(path, out FileKind cached));
            Assert.AreEqual(FileKind.Executable, cached);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DetectAsync_AnUnreadableFileIsUnknownAndNotCached()
    {
        string path = Path.Combine(NewFolder(), "missing.pdf");
        Assert.AreEqual(FileKind.Unknown, await new FileDisguiseChecker().DetectAsync(path));
        Assert.IsFalse(FileDisguiseCache.TryGet(path, out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Listing_MarksADoubleExtensionAndNeverReadsTheFile()
    {
        string folder = NewFolder();
        try
        {
            string path = Path.Combine(folder, "invoice.pdf.exe");
            File.WriteAllText(path, "x");
            string plain = Path.Combine(folder, "notes.txt");
            File.WriteAllText(plain, "x");

            string flagged = RowText(new FileSystemEntry(path));
            string clean = RowText(new FileSystemEntry(plain));

            StringAssert.Contains(flagged, "!");
            Assert.IsFalse(clean.Contains('!'));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Listing_MarksAContentMismatchOnceItHasBeenRead_AndMasksHiddenCharacters()
    {
        string folder = NewFolder();
        try
        {
            string path = Path.Combine(folder, "photo.jpg");
            File.WriteAllText(path, "x");
            FileSystemEntry entry = new(path);
            Assert.IsFalse(RowText(entry).Contains('!'));

            FileDisguiseCache.Set(path, FileKind.Executable);
            StringAssert.Contains(RowText(entry), "!");

            string spoofed = Path.Combine(folder, "doc" + (char)0x202E + "fdp.txt");
            File.WriteAllText(spoofed, "x");
            string row = RowText(new FileSystemEntry(spoofed));
            Assert.IsFalse(row.Contains((char)0x202E), "the override must never reach the terminal");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InfoStrip_ShowsTheMismatchAfterABackgroundRead()
    {
        string folder = NewFolder();
        try
        {
            string path = Path.Combine(folder, "report.pdf");
            await File.WriteAllBytesAsync(path, [0x7F, 0x45, 0x4C, 0x46, 0x02]);

            Mock<IContentModelService> service = new();
            ItemInformation sut = new(
                service.Object,
                new Mock<TaskManager>(MockBehavior.Loose).Object,
                new MemoryCacheService(),
                Mock.Of<IThemeService>(t => t.CurrentColorScheme == new ColorScheme()),
                Mock.Of<ILogger>(),
                new FileDisguiseChecker())
            {
                ViewSize = new Size(100, 1),
            };
            IEntry entry = new FileSystemEntry(path);
            Mock<IContentModel> model = new(MockBehavior.Loose);
            model.Setup(m => m.GetFocusedEntry()).Returns(entry);
            sut.ContentModel = model.Object;

            service.Raise(s => s.ModelChanged += null, EventArgs.Empty);

            for (int i = 0; i < 100 && !sut.Text1.Contains('⚠'); i++)
            {
                await Task.Delay(50);
            }

            StringAssert.Contains(sut.Text1, "⚠");
            StringAssert.Contains(sut.Text1, ".pdf");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string RowText(FileSystemEntry entry)
    {
        ConxelString row = entry.FormatItemName(60, false, new ColorScheme(), VisualState.Focused);
        return new string(row.String.Select(c => c.Char).ToArray());
    }
}
