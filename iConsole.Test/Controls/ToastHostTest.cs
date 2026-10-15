using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class ToastHostTest
{
    private readonly TestThemeService _theme = new();
    private readonly NotificationService _service = new();

    /// <summary>A 60×20 root with one ordinary child and the host, as a screen has them.</summary>
    private (Control Root, ToastHost Host) CreateRoot()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        root.AddControl(new Control(_theme) { ViewSize = new Size(60, 1) });
        var host = new ToastHost(_service, _theme) { MaxToastWidth = 30 };
        root.AddControl(host);
        host.OnInitializeLayout();
        return (root, host);
    }

    private static List<Toast> Toasts(ToastHost host) => [.. host.Controls.AsSnapshot().OfType<Toast>()];

    [TestMethod]
    public void WithNothingPosted_DrawsNothing()
    {
        var (root, host) = CreateRoot();

        host.OnIdle();

        Assert.AreEqual(0, Toasts(host).Count);
        Assert.AreEqual(string.Empty, ScreenBuffer.Render(host, 60, 20).ToString());
    }

    [TestMethod]
    public void Posted_AppearsInTheTopRightCorner_BelowTheFirstRow()
    {
        var (root, host) = CreateRoot();
        _service.Notify("Copy finished", severity: NotificationSeverity.Success);

        host.OnIdle();
        var screen = ScreenBuffer.Render(host, 60, 20);

        // 30 wide, one column free on the right: columns 29..58 of row 1.
        Assert.AreEqual(new Position(29, 1), screen.Find("▌ ✓ Copy finished"));
        Assert.IsFalse(screen.IsWritten(59, 1));
        Assert.AreEqual(string.Empty, screen.Lines[0]);
    }

    [TestMethod]
    public void SeveralPosted_AreStackedOldestFirst_WithABlankRowBetween()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First", "with a message");
        _service.Notify("Second");

        host.OnIdle();
        var screen = ScreenBuffer.Render(host, 60, 20);

        Assert.AreEqual(1, screen.Find("First")!.Y);
        Assert.AreEqual(2, screen.Find("with a message")!.Y);
        Assert.AreEqual(string.Empty, screen.Lines[3]);
        Assert.AreEqual(4, screen.Find("Second")!.Y);
    }

    [TestMethod]
    public void Expired_IsRemovedFromTheTree_AndTheOthersMoveUp()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First", duration: TimeSpan.FromSeconds(1));
        _service.Notify("Second", duration: TimeSpan.FromSeconds(5));
        host.OnIdle();
        var treeVersion = Control.TreeVersion;

        _service.Advance(TimeSpan.FromSeconds(1));
        host.OnIdle();

        Assert.AreEqual(1, Toasts(host).Count);
        Assert.AreEqual("Second", Toasts(host)[0].Notification.Title);
        Assert.AreEqual(1, Toasts(host)[0].Position.Y);

        // What makes the renderer wipe the layer the expired toast was drawn on.
        Assert.AreNotEqual(treeVersion, Control.TreeVersion);
    }

    [TestMethod]
    public void Idle_WithNothingNew_LeavesTheTreeAlone()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First");
        host.OnIdle();
        var toast = Toasts(host)[0];
        var treeVersion = Control.TreeVersion;

        host.OnIdle();
        host.OnIdle();

        // A host that touched the tree on every idle pass would have the renderer repaint the
        // whole screen from scratch for as long as a toast is up.
        Assert.AreEqual(treeVersion, Control.TreeVersion);
        Assert.AreSame(toast, Toasts(host)[0]);
    }

    [TestMethod]
    public void Toasts_AreDrawnOneLayerAboveEverythingElse_AlsoAfterADialogOpens()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First");
        host.OnIdle();
        Assert.AreEqual(1, Toasts(host)[0].ZOrder);

        var dialog = new Control(_theme) { ZOrder = 1, ViewSize = new Size(20, 5) };
        dialog.AddControl(new Control(_theme) { ZOrder = 2 });
        root.AddControl(dialog);
        host.OnIdle();

        Assert.AreEqual(3, Toasts(host)[0].ZOrder);
        Assert.AreEqual(3, root.GetMaxZOrder());

        root.RemoveControl(dialog);
        host.OnIdle();

        Assert.AreEqual(1, Toasts(host)[0].ZOrder, "the layer is given back when the dialog closes");
    }

    [TestMethod]
    public void AToastThatDoesNotFitUnderTheOthers_IsNotDrawn()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 4) };
        var host = new ToastHost(_service, _theme) { MaxToastWidth = 30 };
        root.AddControl(host);
        _service.Notify("First");
        _service.Notify("Second");
        _service.Notify("Third");

        host.OnIdle();
        var screen = ScreenBuffer.Render(host, 60, 4);

        // Rows 1 and 3 hold a toast each; a third would start on row 5.
        Assert.IsTrue(screen.Contains("First"));
        Assert.IsTrue(screen.Contains("Second"));
        Assert.IsFalse(screen.Contains("Third"));
    }

    [TestMethod]
    public void NarrowScreen_NarrowsTheToasts()
    {
        var root = new Control(_theme) { ViewSize = new Size(20, 10) };
        var host = new ToastHost(_service, _theme);
        root.AddControl(host);
        _service.Notify("A title that will not fit");

        host.OnIdle();

        var toast = Toasts(host)[0];
        Assert.AreEqual(19, toast.ViewSize.Width);
        Assert.AreEqual(0, toast.Position.X);
    }

    [TestMethod]
    public async Task Click_OnAToast_DismissesItsNotification()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First");
        var second = _service.Notify("Second");
        host.OnIdle();

        var handled = await host.HandleMouseEventAsync(new MouseEvent { Position = new Position(40, 1), LeftClicked = true });
        host.OnIdle();

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { second }, _service.Active.ToArray());
        Assert.AreEqual(1, Toasts(host).Count);
    }

    [TestMethod]
    public async Task Click_ElsewhereOnTheScreen_IsNotTheHosts()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First");
        host.OnIdle();

        var handled = await host.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 10), LeftClicked = true });

        Assert.IsFalse(handled);
        Assert.AreEqual(1, _service.Active.Count);
    }

    [TestMethod]
    public void TheHost_NeverTakesFocus()
    {
        var (root, host) = CreateRoot();
        _service.Notify("First");
        host.OnIdle();

        Assert.IsFalse(host.CanFocus);
        Assert.IsFalse(host.HasFocusableControl(out _));
    }
}
