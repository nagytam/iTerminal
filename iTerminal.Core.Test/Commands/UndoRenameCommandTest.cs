using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class UndoRenameCommandTest
{
    private static readonly Url Source = new(@"C:\files\old.txt");
    private static readonly Url Target = new(@"C:\files\new.txt");

    private static (Mock<IContentModel> Model, Mock<INotificationService> Notifications) Setup(bool targetExists, bool sourceTaken)
    {
        Mock<IContentModel> model = new();
        model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == Target.FullUrl))).Returns(targetExists);
        model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == Source.FullUrl))).Returns(sourceTaken);
        model.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>())).Returns(Task.CompletedTask);
        return (model, new Mock<INotificationService>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingRemembered_CannotExecute()
    {
        var command = new UndoRenameCommand(new RenameUndoService());

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AfterARename_MovesTheEntryBackAndForgetsIt()
    {
        var undo = new RenameUndoService();
        var (model, notifications) = Setup(targetExists: true, sourceTaken: false);
        undo.Remember(model.Object, Source, Target);
        var command = new UndoRenameCommand(undo, notifications.Object);

        Assert.IsTrue(command.CanExecute());
        await command.ExecuteAsync(null);

        model.Verify(m => m.MoveAsync(
            It.Is<Url>(u => u.FullUrl == Target.FullUrl),
            It.Is<Url>(u => u.FullUrl == Source.FullUrl),
            false), Times.Once);
        model.Verify(m => m.Refresh(false), Times.Once);
        Assert.IsFalse(command.CanExecute());
        notifications.VerifyNoOtherCalls();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheRenamedEntryIsGone_SaysSoAndMovesNothing()
    {
        var undo = new RenameUndoService();
        var (model, notifications) = Setup(targetExists: false, sourceTaken: false);
        undo.Remember(model.Object, Source, Target);
        var command = new UndoRenameCommand(undo, notifications.Object);

        await command.ExecuteAsync(null);

        model.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOldNameIsTaken_KeepsBothAndSaysSo()
    {
        var undo = new RenameUndoService();
        var (model, notifications) = Setup(targetExists: true, sourceTaken: true);
        undo.Remember(model.Object, Source, Target);
        var command = new UndoRenameCommand(undo, notifications.Object);

        await command.ExecuteAsync(null);

        model.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RememberReplacesThePreviousRename()
    {
        var undo = new RenameUndoService();
        var (model, _) = Setup(targetExists: true, sourceTaken: false);
        undo.Remember(model.Object, Source, Target);
        var second = new Url(@"C:\files\other.txt");
        undo.Remember(model.Object, Target, second);

        Assert.AreEqual(second.FullUrl, undo.Peek()!.Target.FullUrl);
    }
}
