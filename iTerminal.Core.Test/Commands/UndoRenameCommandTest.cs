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

    private static (Url Source, Url Target) Pair(string name) => (new Url($@"C:\files\{name}-old.txt"), new Url($@"C:\files\{name}-new.txt"));

    [Timeout(60_000)]
    [TestMethod]
    public async Task AfterAMultiRename_PutsEveryNameBackInReverseOrder()
    {
        var undo = new RenameUndoService();
        var (a, b, c) = (Pair("a"), Pair("b"), Pair("c"));
        Mock<IContentModel> model = new();
        foreach (var pair in new[] { a, b, c })
        {
            model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == pair.Target.FullUrl))).Returns(true);
        }

        List<string> movedFrom = [];
        model.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), false))
            .Callback<Url, Url, bool>((from, _, _) => movedFrom.Add(from.FullUrl))
            .Returns(Task.CompletedTask);
        Mock<INotificationService> notifications = new();
        undo.RememberBatch(model.Object, [a, b, c]);
        var command = new UndoRenameCommand(undo, notifications.Object);

        Assert.IsTrue(command.CanExecute());
        await command.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { c.Target.FullUrl, b.Target.FullUrl, a.Target.FullUrl }, movedFrom);
        model.Verify(m => m.Refresh(false), Times.Once);
        Assert.IsFalse(command.CanExecute());
        notifications.VerifyNoOtherCalls();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AfterAMultiRename_AnEntryThatCannotBePutBackDoesNotStopTheOthers()
    {
        var undo = new RenameUndoService();
        var (gone, taken, fine) = (Pair("gone"), Pair("taken"), Pair("fine"));
        Mock<IContentModel> model = new();
        model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == taken.Target.FullUrl))).Returns(true);
        model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == taken.Source.FullUrl))).Returns(true);
        model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == fine.Target.FullUrl))).Returns(true);
        model.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), false)).Returns(Task.CompletedTask);
        Mock<INotificationService> notifications = new();
        undo.RememberBatch(model.Object, [gone, taken, fine]);
        var command = new UndoRenameCommand(undo, notifications.Object);

        await command.ExecuteAsync(null);

        model.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Once);
        model.Verify(m => m.MoveAsync(
            It.Is<Url>(u => u.FullUrl == fine.Target.FullUrl),
            It.Is<Url>(u => u.FullUrl == fine.Source.FullUrl),
            false), Times.Once);
        notifications.Verify(n => n.Notify(
            It.IsAny<string>(),
            It.Is<string?>(message => message != null && message.Contains("1 of 3")),
            NotificationSeverity.Warning,
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RememberBatch_OfNothing_KeepsWhatWasRememberedBefore()
    {
        var undo = new RenameUndoService();
        var (model, _) = Setup(targetExists: true, sourceTaken: false);
        undo.Remember(model.Object, Source, Target);

        undo.RememberBatch(model.Object, []);

        Assert.AreEqual(Target.FullUrl, undo.Peek()!.Target.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TakeAll_ReturnsTheWholeStepOnceAndForgetsIt()
    {
        var undo = new RenameUndoService();
        var (model, _) = Setup(targetExists: true, sourceTaken: false);
        undo.RememberBatch(model.Object, [Pair("a"), Pair("b")]);

        Assert.AreEqual(2, undo.TakeAll().Count);
        Assert.AreEqual(0, undo.TakeAll().Count);
        Assert.IsNull(undo.Peek());
    }
}
