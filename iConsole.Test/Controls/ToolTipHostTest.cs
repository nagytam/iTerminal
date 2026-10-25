using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class ToolTipHostTest
{
    private readonly TestThemeService _theme = new();
    private readonly VirtualTimeProvider _time = new();
    private readonly ToolTipService _tips = new();

    /// <summary>A 60×20 root with a 10-wide button that has a tooltip, and the host, as a screen has them.</summary>
    private (Control Root, Control Button, ToolTipHost Host) CreateRoot()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        var button = new Control(_theme) { Position = new Position(2, 2), ViewSize = new Size(10, 1), ToolTipText = "Copy file" };
        root.AddControl(button);
        var host = new ToolTipHost(_tips, _time, _theme);
        root.AddControl(host);
        host.OnInitializeLayout();
        return (root, button, host);
    }

    [TestMethod]
    public void RestingOnAControl_ShowsItsTipOnlyAfterTheDelay()
    {
        var (root, button, host) = CreateRoot();

        _tips.ReportPointer(new Position(4, 2));
        host.Synchronize();
        Assert.AreSame(button, host.Target);
        Assert.IsFalse(host.IsShowing);

        _time.Advance(host.Delay - TimeSpan.FromMilliseconds(1));
        host.Synchronize();
        Assert.IsFalse(host.IsShowing, "One millisecond before the delay the tip is still waiting.");

        _time.Advance(TimeSpan.FromMilliseconds(1));
        host.Synchronize();
        Assert.IsTrue(host.IsShowing);
        Assert.IsNotNull(ScreenBuffer.Render(root, 60, 20).Find(" Copy file "));
    }

    [TestMethod]
    public void MovingOffTheControl_HidesTheTip()
    {
        var (root, _, host) = CreateRoot();
        _tips.ReportPointer(new Position(4, 2));
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();
        Assert.IsTrue(host.IsShowing);

        _tips.ReportPointer(new Position(40, 15));
        host.Synchronize();

        Assert.IsNull(host.Target);
        Assert.IsFalse(host.IsShowing);
    }

    [TestMethod]
    public void AClickOrAKey_DismissesTheTip_UntilThePointerMovesAgain()
    {
        var (_, _, host) = CreateRoot();
        _tips.ReportPointer(new Position(4, 2));
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();

        _tips.Dismiss();
        host.Synchronize();
        Assert.IsFalse(host.IsShowing);

        // Still on the button, but nothing is re-armed until the pointer moves.
        _time.Advance(host.Delay);
        host.Synchronize();
        Assert.IsFalse(host.IsShowing);

        _tips.ReportPointer(new Position(5, 2));
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();
        Assert.IsTrue(host.IsShowing);
    }

    [TestMethod]
    public void TheInnermostControlWithText_Wins()
    {
        var (root, button, host) = CreateRoot();
        var inner = new Control(_theme) { Position = new Position(1, 0), ViewSize = new Size(3, 1), ToolTipText = "Inner" };
        button.AddControl(inner);

        var target = ToolTipHost.FindTarget(root, new Position(3, 2));

        Assert.AreSame(inner, target);
    }

    [TestMethod]
    public void AControlWithoutText_HasNoTarget()
    {
        var (root, _, _) = CreateRoot();

        Assert.IsNull(ToolTipHost.FindTarget(root, new Position(40, 15)));
    }

    [TestMethod]
    public void AHiddenControl_IsNotATarget()
    {
        var (root, button, _) = CreateRoot();
        button.IsVisible = false;

        Assert.IsNull(ToolTipHost.FindTarget(root, new Position(4, 2)));
    }

    [TestMethod]
    public void TipNearTheBottomRight_OpensAboveAndLeftOfThePointer()
    {
        var (root, button, host) = CreateRoot();
        button.Position = new Position(55, 18);
        button.ViewSize = new Size(4, 1);
        var pointer = new Position(56, 18);

        _tips.ReportPointer(pointer);
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();

        Assert.IsTrue(host.IsShowing);
        var tip = ScreenBuffer.Render(root, 60, 20).Find(" Copy file ");
        Assert.IsNotNull(tip);
        Assert.IsTrue(tip.Y < pointer.Y,"The tip sits above the pointer row, so the pointer is not covered.");
    }

    [TestMethod]
    public void ChangingTheTargetsText_ReplacesTheShownTip()
    {
        var (root, button, host) = CreateRoot();
        _tips.ReportPointer(new Position(4, 2));
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();
        Assert.IsTrue(host.IsShowing);

        button.ToolTipText = "Paste";
        host.Synchronize();
        Assert.IsFalse(host.IsShowing, "The stale tip comes down as soon as its text no longer matches.");

        _time.Advance(host.Delay);
        host.Synchronize();
        Assert.IsTrue(host.IsShowing);
        Assert.IsNotNull(ScreenBuffer.Render(root, 60, 20).Find(" Paste "));
    }

    [TestMethod]
    public void ClearingTheTargetsText_HidesItsTip()
    {
        var (_, button, host) = CreateRoot();
        _tips.ReportPointer(new Position(4, 2));
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();

        button.ToolTipText = null;
        host.Synchronize();
        _time.Advance(host.Delay);
        host.Synchronize();

        Assert.IsFalse(host.IsShowing);
    }
}
