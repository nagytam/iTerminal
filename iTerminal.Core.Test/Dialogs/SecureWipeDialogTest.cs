using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Wipe;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class SecureWipeDialogTest
{
    private Mock<ISecureWipeService> _wipe = null!;
    private NotificationService _notifications = null!;
    private SecureWipeDialog _dialog = null!;

    /// <summary>A panel service with no panels: the dialogs re-read every panel when they finish.</summary>
    private static iConsole.Interfaces.IContentModelService Models()
    {
        var service = new Mock<iConsole.Interfaces.IContentModelService>();
        service.SetupGet(s => s.ContentModels).Returns([]);
        return service.Object;
    }

    /// <summary>The dialog redraws from its idle pass; a test has to give it one before it reads the screen.</summary>
    private iConsole.Test.Core.ScreenBuffer Screen()
    {
        _dialog.OnIdle();
        return UiTestHelper.Render(_dialog);
    }

    [TestInitialize]
    public void Setup()
    {
        _wipe = new Mock<ISecureWipeService>();
        _notifications = new NotificationService();
        _dialog = new SecureWipeDialog(_wipe.Object, _notifications, Models(), UiTestHelper.TestTheme());
    }

    private async Task OpenAsync(params string[] paths)
    {
        _dialog.Load(paths);
        await UiTestHelper.ShowAsync(_dialog);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Opens_NamingWhatWillBeDestroy_AndWarningThatItCannotBeUndone()
    {
        await OpenAsync(@"C:\data\secret.txt");

        var screen = Screen();
        UiTestHelper.AssertScreenContains(screen, "secret.txt");
        UiTestHelper.AssertScreenContains(screen, "cannot be undone");
        UiTestHelper.AssertScreenContains(screen, "SSD");
        Assert.AreEqual(WipePhase.Choosing, _dialog.Phase);
        _wipe.Verify(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task OneEnter_OnlyArms_NothingIsWiped()
    {
        await OpenAsync(@"C:\data\secret.txt");

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));

        Assert.AreEqual(WipePhase.Armed, _dialog.Phase);
        _wipe.Verify(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
        UiTestHelper.AssertScreenContains(Screen(), "Press Enter again");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AnyOtherKeyAfterArming_WithdrawsIt()
    {
        await OpenAsync(@"C:\data\secret.txt");
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Spacebar, ' '));

        Assert.AreEqual(WipePhase.Choosing, _dialog.Phase);
        _wipe.Verify(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task TwoEnters_WipeWithTheChosenMethod_AndSayHowItEnded()
    {
        _wipe.Setup(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WipeResult(2, 1, 2048, [], Cancelled: false));
        await OpenAsync(@"C:\data\a.txt", @"C:\data\b.txt");

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.D3, '3'));
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));
        await _dialog.WipeTask;

        Assert.AreEqual(WipePhase.Finished, _dialog.Phase);
        _wipe.Verify(w => w.WipeAsync(
            It.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { @"C:\data\a.txt", @"C:\data\b.txt" })),
            WipePattern.ThreePass,
            It.IsAny<IProgress<WipeProgress>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(NotificationSeverity.Success, _notifications.History.Single().Severity);
        UiTestHelper.AssertScreenContains(Screen(), "Done: 2 file(s)");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AFailure_IsListedAndPostedAsAnError()
    {
        _wipe.Setup(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WipeResult(0, 0, 0, ["a.txt: The process cannot access the file"], Cancelled: false));
        await OpenAsync(@"C:\data\a.txt");

        _dialog.Arm();
        _dialog.Confirm();
        await _dialog.WipeTask;

        Assert.AreEqual(NotificationSeverity.Error, _notifications.History.Single().Severity);
        UiTestHelper.AssertScreenContains(Screen(), "cannot access");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Closing_WhileItRuns_CancelsTheWipe()
    {
        var token = CancellationToken.None;
        var started = new TaskCompletionSource();
        _wipe.Setup(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<string>, WipePattern, IProgress<WipeProgress>?, CancellationToken>(async (_, _, _, t) =>
            {
                token = t;
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, t);
                }
                catch (OperationCanceledException)
                {
                }

                return new WipeResult(0, 0, 0, [], Cancelled: true);
            });
        await OpenAsync(@"C:\data\a.txt");
        _dialog.Arm();
        _dialog.Confirm();
        await started.Task;

        _dialog.Close();
        await _dialog.WipeTask;

        Assert.IsTrue(token.IsCancellationRequested);
        Assert.AreEqual(NotificationSeverity.Warning, _notifications.History.Single().Severity);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ChangingTheMethodAfterArming_GoesBackToChoosing()
    {
        await OpenAsync(@"C:\data\a.txt");
        _dialog.Arm();

        _dialog.ChoosePattern(WipePattern.SevenPass);

        Assert.AreEqual(WipePhase.Choosing, _dialog.Phase);
        Assert.AreEqual(WipePattern.SevenPass, _dialog.Pattern);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task TheArrows_StepThroughTheMethods_AndStopAtTheEnds()
    {
        await OpenAsync(@"C:\data.txt");
        Assert.AreEqual(WipePattern.Random, _dialog.Pattern);

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.UpArrow));
        Assert.AreEqual(WipePattern.Random, _dialog.Pattern);
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.DownArrow));
        Assert.AreEqual(WipePattern.ThreePass, _dialog.Pattern);
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.RightArrow));
        Assert.AreEqual(WipePattern.SevenPass, _dialog.Pattern);
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.DownArrow));
        Assert.AreEqual(WipePattern.SevenPass, _dialog.Pattern);
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(WipePattern.ThreePass, _dialog.Pattern);
        UiTestHelper.AssertScreenContains(Screen(), "3 passes");
        UiTestHelper.AssertScreenContains(Screen(), "(or ↑↓)");
        Assert.AreEqual(WipePhase.Choosing, _dialog.Phase);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WhenArmed_AnArrowWithdrawsInsteadOfChangingTheMethod()
    {
        await OpenAsync(@"C:\data.txt");
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));
        Assert.AreEqual(WipePhase.Armed, _dialog.Phase);

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.DownArrow));

        Assert.AreEqual(WipePhase.Choosing, _dialog.Phase);
        Assert.AreEqual(WipePattern.Random, _dialog.Pattern);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Enter_ClosesTheDialogOnceTheWipeIsOver()
    {
        _wipe.Setup(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WipeResult(1, 0, 10, [], Cancelled: false));
        await OpenAsync(@"C:\data.txt");
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));
        await _dialog.WipeTask;
        Assert.AreEqual(WipePhase.Finished, _dialog.Phase);
        Assert.IsTrue(_dialog.IsVisible);

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Enter));

        Assert.IsFalse(_dialog.IsVisible);
    }
}
