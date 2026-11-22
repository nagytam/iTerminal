using iConsole.Media.Speech;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Commands.Evidence;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Evidence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands.Evidence;

[TestClass]
public class EvidenceCommandsTest
{
    private static readonly string CaseFolder = Path.Combine(Path.GetTempPath(), "case-42");

    private static EvidenceRecord Record(int sequence, string name) =>
        new(sequence, name, EvidenceKind.File, name, 10, new string('a', 64), new EvidenceOrigin(
            "file:///a.txt", "C:", 10, null, "test-machine", "tester", "Test OS", "1.0", [], DateTime.UtcNow, "+00:00"));

    [TestMethod]
    public void Resolve_IsNullForBlankAndForAPathThatCannotExist()
    {
        Assert.IsNull(EvidenceNotebookOpening.Resolve("   "));
        Assert.IsNull(EvidenceNotebookOpening.Resolve("bad\0name"));
    }

    [TestMethod]
    public void Resolve_TakesATildeAsTheHomeFolderAndMakesAPathFull()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.AreEqual(Path.GetFullPath(Path.Combine(home, "cases", "case-42")), EvidenceNotebookOpening.Resolve("~/cases/case-42"));
        Assert.AreEqual(Path.GetFullPath("relative-case"), EvidenceNotebookOpening.Resolve("relative-case"));
    }

    [TestMethod]
    public async Task EnsureOpen_UsesAnOpenNotebookWithoutAsking()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(true);
        var prompts = new Mock<IPromptService>();

        Assert.IsTrue(await EvidenceNotebookOpening.EnsureOpenAsync(prompts.Object, Mock.Of<ISettingsService>(), notebook.Object, null));

        prompts.Verify(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        notebook.Verify(n => n.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ChooseAndOpen_CancelledOpensNothing()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        var prompts = new Mock<IPromptService>();
        prompts.Setup(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.IsFalse(await EvidenceNotebookOpening.ChooseAndOpenAsync(prompts.Object, Mock.Of<ISettingsService>(), notebook.Object, null));

        notebook.Verify(n => n.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ChooseAndOpen_OpensTheTypedFolderAndRemembersIt()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        var prompts = new Mock<IPromptService>();
        prompts.Setup(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CaseFolder);
        var settings = new Mock<ISettingsService>();

        bool opened = await EvidenceNotebookOpening.ChooseAndOpenAsync(prompts.Object, settings.Object, notebook.Object, null);

        Assert.IsTrue(opened);
        string full = Path.GetFullPath(CaseFolder);
        notebook.Verify(n => n.OpenAsync(full, It.IsAny<CancellationToken>()), Times.Once);
        settings.Verify(s => s.Set(EvidenceNotebookOpening.FolderSettingKey, full), Times.Once);
        settings.Verify(s => s.Write(), Times.Once);
    }

    [TestMethod]
    public async Task ChooseAndOpen_AFolderThatCannotBeReadSaysWhyAndIsNotRemembered()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("index version 9 is not readable"));
        var prompts = new Mock<IPromptService>();
        prompts.Setup(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(CaseFolder);
        var settings = new Mock<ISettingsService>();
        var notifications = new Mock<INotificationService>();

        bool opened = await EvidenceNotebookOpening.ChooseAndOpenAsync(prompts.Object, settings.Object, notebook.Object, notifications.Object);

        Assert.IsFalse(opened);
        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        notifications.Verify(n => n.Notify(It.IsAny<string>(), "index version 9 is not readable", NotificationSeverity.Error), Times.Once);
    }

    [TestMethod]
    public async Task Verify_WithoutANotebookSaysSo()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(false);
        var notifications = new Mock<INotificationService>();

        await new VerifyEvidenceNotebookCommand(notebook.Object, notifications.Object).ExecuteAsync(null);

        notifications.Verify(n => n.Notify(It.IsAny<string>(), null, NotificationSeverity.Warning), Times.Once);
        notebook.Verify(n => n.VerifyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Verify_ACleanNotebookReportsEveryEntry()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(true);
        notebook.Setup(n => n.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Record(1, "a.txt"), Record(2, "b.txt")]);
        notebook.Setup(n => n.VerifyAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var notifications = new Mock<INotificationService>();

        await new VerifyEvidenceNotebookCommand(notebook.Object, notifications.Object).ExecuteAsync(null);

        notifications.Verify(n => n.Notify(It.Is<string>(t => t.Contains("2")), null, NotificationSeverity.Success), Times.Once);
    }

    [TestMethod]
    public async Task Verify_AChangedEntryIsReportedWithItsFileName()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(true);
        notebook.Setup(n => n.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Record(1, "a.txt"), Record(2, "b.txt")]);
        notebook.Setup(n => n.VerifyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EvidenceProblem("b.txt", "the copy changed")]);
        var notifications = new Mock<INotificationService>();

        await new VerifyEvidenceNotebookCommand(notebook.Object, notifications.Object).ExecuteAsync(null);

        notifications.Verify(n => n.Notify(It.Is<string>(t => t.Contains("1")), "b.txt: the copy changed", NotificationSeverity.Error), Times.Once);
    }

    private static Mock<IPromptService> PromptsChoosing(AddEvidenceCommand.Choice choice)
    {
        var prompts = new Mock<IPromptService>();
        prompts.Setup(p => p.SelectAsync(It.IsAny<SelectionPrompt<AddEvidenceCommand.Choice?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AddEvidenceCommand.Choice?)choice);
        return prompts;
    }

    private static async Task WaitForNotificationAsync(Mock<INotificationService> notifications)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (notifications.Invocations.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task AddNote_RecordsTheNoteAndSaysWhenItHasNoTimeStamp()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(true);
        notebook.Setup(n => n.AddTextAsync(EvidenceKind.Note, "the witness left at noon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, "note-1.txt"));
        var prompts = PromptsChoosing(AddEvidenceCommand.Choice.Note);
        prompts.Setup(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync("the witness left at noon");
        var notifications = new Mock<INotificationService>();
        var command = NewAddCommand(prompts, notebook, notifications: notifications);

        await command.ExecuteAsync(null);
        await WaitForNotificationAsync(notifications);

        notebook.Verify(n => n.AddTextAsync(EvidenceKind.Note, "the witness left at noon", It.IsAny<CancellationToken>()), Times.Once);
        // The fake record carries no RFC 3161 time stamp, so the person is told the entry is kept but not stamped.
        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), NotificationSeverity.Warning), Times.Once);
    }

    [TestMethod]
    public async Task AddNote_AFailedWriteIsReportedAndNotThrown()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(true);
        notebook.Setup(n => n.AddTextAsync(It.IsAny<EvidenceKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("the disk is full"));
        var prompts = PromptsChoosing(AddEvidenceCommand.Choice.Note);
        prompts.Setup(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync("a note");
        var notifications = new Mock<INotificationService>();
        var command = NewAddCommand(prompts, notebook, notifications: notifications);

        await command.ExecuteAsync(null);
        await WaitForNotificationAsync(notifications);

        notifications.Verify(n => n.Notify(It.IsAny<string>(), "the disk is full", NotificationSeverity.Error), Times.Once);
    }

    [TestMethod]
    public async Task AddClipboard_AnEmptyClipboardSaysSoAndAddsNothing()
    {
        var notebook = new Mock<IEvidenceNotebookService>();
        notebook.Setup(n => n.IsOpen).Returns(true);
        var clipboard = new Mock<ITextClipboardService>();
        clipboard.Setup(c => c.GetTextAsync()).ReturnsAsync(string.Empty);
        var notifications = new Mock<INotificationService>();
        var command = NewAddCommand(PromptsChoosing(AddEvidenceCommand.Choice.Clipboard), notebook, clipboard, notifications);

        await command.ExecuteAsync(null);
        await WaitForNotificationAsync(notifications);

        notifications.Verify(n => n.Notify(It.IsAny<string>(), null, NotificationSeverity.Warning), Times.Once);
        notebook.Verify(n => n.AddTextAsync(It.IsAny<EvidenceKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AddEvidenceCommand NewAddCommand(
        Mock<IPromptService> prompts,
        Mock<IEvidenceNotebookService> notebook,
        Mock<ITextClipboardService>? clipboard = null,
        Mock<INotificationService>? notifications = null)
    {
        var contentModels = new Mock<iConsole.Interfaces.IContentModelService>();
        contentModels.Setup(c => c.ContentModels).Returns(new iConsole.Data.ConcurrentList<iConsole.Interfaces.IContentModel>());
        return new AddEvidenceCommand(
            contentModels.Object,
            notebook.Object,
            prompts.Object,
            Mock.Of<ISettingsService>(),
            (clipboard ?? new Mock<ITextClipboardService>()).Object,
            Mock.Of<IMicrophoneCaptureFactory>(),
            null!,
            notifications?.Object);
    }
}

