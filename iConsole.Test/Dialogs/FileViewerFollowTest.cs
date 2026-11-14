using System.Text;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>F6 follows a local file as it grows: whole new lines are added at the end, the cursor goes with them, and the follow stops when the file shrinks.</summary>
[TestClass]
public class FileViewerFollowTest
{
    private string _file = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.GetTempFileName();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static Url FileUrl(string path) => new("file://" + path.Replace('\\', '/'));

    private static FileViewerDialog NewDialog(Url? url)
    {
        var read = new Mock<IFileToTextConverterService>();
        read.Setup(f => f.ReadFileAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<TextModel>()))
            .Returns((Url? u, IFileSystem _, TextModel model) =>
            {
                // A stand-in reader: the file's text, split the way the model keeps it (the last element is what follows the final break).
                var text = u is null ? string.Empty : File.ReadAllText(u.Path);
                foreach (var line in text.Split('\n'))
                {
                    model.Lines.Add(line.TrimEnd('\r'));
                }

                return Task.CompletedTask;
            });
        var dialog = new FileViewerDialog(
            Mock.Of<IContentModelService>(),
            read.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService());
        dialog.Url = url;
        return dialog;
    }

    private static string Lines(FileViewerDialog dialog) => string.Join("|", dialog.TextViewerControl.TextModel.Lines.AsSnapshot());

    private async Task<FileViewerDialog> OpenAsync(string initial)
    {
        File.WriteAllText(_file, initial);
        var dialog = NewDialog(FileUrl(_file));
        await dialog.OnInitializeAsync();
        return dialog;
    }

    /// <summary>Lets the idle loop start a follow check and waits for the worker to finish it, then applies it.</summary>
    private static async Task CheckAsync(FileViewerDialog dialog)
    {
        // The follow checks at most once a second, so wait out that interval before each check.
        await Task.Delay(1100);
        dialog.OnIdle();
        await dialog.PendingFollow;
        dialog.OnIdle();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Follow_AddsTheNewLinesAndMovesTheCursorToTheLast()
    {
        var dialog = await OpenAsync("one\ntwo\n");
        Assert.IsTrue(dialog.ToggleFollow());

        File.AppendAllText(_file, "three\nfour\n");
        await CheckAsync(dialog);

        Assert.AreEqual("one|two|three|four|", Lines(dialog));
        Assert.AreEqual(dialog.TextViewerControl.TextModel.Lines.Count - 1, dialog.TextViewerControl.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Follow_WaitsForTheEndOfALine_BeforeAddingIt()
    {
        var dialog = await OpenAsync("one\n");
        dialog.ToggleFollow();

        File.AppendAllText(_file, "tw");
        await CheckAsync(dialog);
        Assert.AreEqual("one|", Lines(dialog), "a partial line is not shown yet");

        File.AppendAllText(_file, "o\nthree\n");
        await CheckAsync(dialog);
        Assert.AreEqual("one|two|three|", Lines(dialog), "the partial line is completed, not duplicated");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutFollow_AppendedTextIsNotAdded()
    {
        var dialog = await OpenAsync("one\n");
        File.AppendAllText(_file, "two\n");

        await CheckAsync(dialog);

        Assert.AreEqual("one|", Lines(dialog));
        Assert.IsFalse(dialog.IsFollowing);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F6_StartsAndStopsTheFollow()
    {
        var dialog = await OpenAsync("one\n");

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F6, false, false, false));
        Assert.IsTrue(dialog.IsFollowing);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F6, false, false, false));
        Assert.IsFalse(dialog.IsFollowing);

        File.AppendAllText(_file, "two\n");
        await CheckAsync(dialog);
        Assert.AreEqual("one|", Lines(dialog), "after F6 stops the follow the file is not read again");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANonLocalFile_CannotBeFollowed()
    {
        var dialog = NewDialog(new Url("memdrive://notes.txt"));
        Assert.IsFalse(dialog.ToggleFollow());
        Assert.IsFalse(dialog.IsFollowing);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatGetsShorter_StopsTheFollow_AndKeepsTheText()
    {
        var dialog = await OpenAsync("one\ntwo\nthree\n");
        dialog.ToggleFollow();

        File.WriteAllText(_file, "x\n");
        await CheckAsync(dialog);

        Assert.IsFalse(dialog.IsFollowing, "a truncated or rotated file is not followed on");
        Assert.AreEqual("one|two|three|", Lines(dialog));
    }
}
