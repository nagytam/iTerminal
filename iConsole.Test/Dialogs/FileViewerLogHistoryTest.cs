using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>
/// The F3 viewer keeps the logs it has shown, and Ctrl+PageUp and Ctrl+PageDown step to the older and newer one. Only a
/// local <c>.log</c> file enters the history.
/// </summary>
[TestClass]
public class FileViewerLogHistoryTest
{
    private readonly List<string> _files = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string file in _files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Url FileUrl(string path) => new("file://" + path.Replace('\\', '/'));

    private string TempLog(string name, string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"viewer-history-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, text);
        _files.Add(path);
        return path;
    }

    /// <summary>A history that keeps what it is given, newest first, and that answers the way the app's does.</summary>
    private sealed class FakeHistory : ILogFileHistory
    {
        public List<Url> Logs { get; } = [];

        public void Record(Url url)
        {
            Logs.RemoveAll(log => log.FullUrl == url.FullUrl);
            Logs.Insert(0, url);
        }

        public Url? Older(Url current)
        {
            int index = Logs.FindIndex(log => log.FullUrl == current.FullUrl);
            return index >= 0 && index + 1 < Logs.Count ? Logs[index + 1] : null;
        }

        public Url? Newer(Url current)
        {
            int index = Logs.FindIndex(log => log.FullUrl == current.FullUrl);
            return index > 0 ? Logs[index - 1] : null;
        }
    }

    private static FileViewerDialog NewDialog(ILogFileHistory history)
    {
        Mock<IFileToTextConverterService> read = new();
        read.Setup(f => f.ReadFileAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<TextModel>()))
            .Returns((Url? u, IFileSystem _, TextModel model) =>
            {
                model.Lines.Clear();
                foreach (string line in File.ReadAllText(u!.Path).Split('\n'))
                {
                    model.Lines.Add(line.TrimEnd('\r'));
                }

                return Task.CompletedTask;
            });
        return new FileViewerDialog(
            Mock.Of<IContentModelService>(),
            read.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            Theme(),
            history);
    }

    /// <summary>Opens the viewer on <paramref name="path"/> the way the F3 command does: through the dialog's own show.</summary>
    private static async Task<FileViewerDialog> OpenAsync(FileViewerDialog dialog, string path)
    {
        dialog.Url = FileUrl(path);
        Control root = new Window(Theme()) { ViewSize = new Size(100, 30) };
        await root.ShowDialogAsync(dialog);
        return dialog;
    }

    private static string Text(FileViewerDialog dialog) => string.Join("|", dialog.TextViewerControl.TextModel.Lines.AsSnapshot());

    private static Task Press(FileViewerDialog dialog, ConsoleKey key, bool control) =>
        dialog.HandleKeyEventAsync(new KeyEvent('\0', key, false, false, control));

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningALog_RecordsIt_AndOpeningOtherFiles_DoesNot()
    {
        FakeHistory history = new();
        string log = TempLog("server.log", "a log line");
        string text = TempLog("notes.txt", "a text line");

        await OpenAsync(NewDialog(history), log);
        await OpenAsync(NewDialog(history), text);

        Assert.AreEqual(1, history.Logs.Count);
        Assert.AreEqual(FileUrl(log).FullUrl, history.Logs[0].FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlPageUp_ShowsTheOlderLog_AndCtrlPageDown_ShowsTheNewerOne()
    {
        FakeHistory history = new();
        string first = TempLog("first.log", "first log");
        string second = TempLog("second.log", "second log");
        history.Record(FileUrl(first));
        history.Record(FileUrl(second));
        FileViewerDialog dialog = await OpenAsync(NewDialog(history), second);

        await Press(dialog, ConsoleKey.PageUp, control: true);
        StringAssert.Contains(Text(dialog), "first log");

        await Press(dialog, ConsoleKey.PageDown, control: true);
        StringAssert.Contains(Text(dialog), "second log");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSwitch_ReadsTheSwitchedLog_AndLeavesTheHistoryOrderAlone()
    {
        FakeHistory history = new();
        string first = TempLog("one.log", "one");
        string second = TempLog("two.log", "two");
        history.Record(FileUrl(first));
        history.Record(FileUrl(second));
        FileViewerDialog dialog = await OpenAsync(NewDialog(history), second);

        await dialog.ShowLogAsync(FileUrl(first));

        Assert.AreEqual(FileUrl(second).FullUrl, history.Logs[0].FullUrl, "a switch is not an open, so the newest stays the newest");
        Assert.AreEqual("one", Text(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCommands_CannotRun_AtTheEndsOfTheHistory()
    {
        FakeHistory history = new();
        string only = TempLog("only.log", "only");
        history.Record(FileUrl(only));
        FileViewerDialog dialog = await OpenAsync(NewDialog(history), only);

        Assert.IsNull(dialog.OlderLog);
        Assert.IsNull(dialog.NewerLog);
        await Press(dialog, ConsoleKey.PageUp, control: true);
        Assert.AreEqual("only", Text(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATextFile_HasNoLogSteps()
    {
        FakeHistory history = new();
        string log = TempLog("a.log", "a");
        string text = TempLog("b.txt", "b");
        history.Record(FileUrl(log));
        history.Record(FileUrl(text));
        FileViewerDialog dialog = await OpenAsync(NewDialog(history), text);

        Assert.IsFalse(dialog.IsShowingLog);
        Assert.IsNull(dialog.OlderLog);
    }

    [TestMethod]
    public void WithoutAHistory_TheViewerStillOpens_AndHasNoLogSteps()
    {
        FileViewerDialog dialog = NewDialog(null!);

        Assert.IsNull(dialog.OlderLog);
        Assert.IsNull(dialog.NewerLog);
    }
}
