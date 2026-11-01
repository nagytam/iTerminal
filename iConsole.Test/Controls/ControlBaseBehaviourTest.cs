using iConsole.Controls;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>What every control inherits from <see cref="Control"/>: rendering, modality, colours, children, idling.</summary>
[TestClass]
public class ControlBaseBehaviourTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private sealed class Probe(IThemeService theme) : Control(theme)
    {
        public int Draws { get; private set; }

        public int Idles { get; private set; }

        public void Touch() => StateHasChanged();

        public bool ThrowOnDraw { get; set; }

        public bool ThrowOnIdle { get; set; }

        public override void Draw(iConsole.Buffers.IBuffer buffer)
        {
            Draws++;
            if (ThrowOnDraw)
            {
                throw new InvalidOperationException("draw failed");
            }
        }

        public override void OnIdle()
        {
            Idles++;
            if (ThrowOnIdle)
            {
                throw new InvalidOperationException("idle failed");
            }

            base.OnIdle();
        }
    }

    // ---- Render ----

    [Timeout(60_000)]
    [TestMethod]
    public void Render_DrawsAChangedControl_AndGoesIdleAfterwards()
    {
        Probe probe = new(Theme()) { ViewSize = new Size(5, 1) };
        MockBuffer buffer = new(new Size(5, 1));
        Assert.IsFalse(probe.Render(buffer), "a changed control is not idle");
        Assert.AreEqual(1, probe.Draws);
        Assert.IsTrue(probe.Render(buffer), "nothing changed since");
        Assert.AreEqual(1, probe.Draws);
        probe.Touch();
        probe.Render(buffer);
        Assert.AreEqual(2, probe.Draws);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_ADrawnControlDrawsNothingWhileHidden_ThenOnceShown()
    {
        Probe probe = new(Theme()) { ViewSize = new Size(5, 1) };
        probe.IsVisible = false;
        MockBuffer buffer = new(new Size(5, 1));
        Assert.IsTrue(probe.Render(buffer));
        Assert.AreEqual(0, probe.Draws);
        probe.IsVisible = true;
        probe.Render(buffer);
        Assert.AreEqual(1, probe.Draws);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_AControlThatThrowsInDraw_DoesNotTakeTheScreenDown_AndIsTriedAgain()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        Probe bad = new(Theme()) { ThrowOnDraw = true, ViewSize = new Size(5, 1) };
        Probe good = new(Theme()) { ViewSize = new Size(5, 1) };
        root.AddControl(bad);
        root.AddControl(good);
        MockBuffer buffer = new(new Size(10, 3));

        root.Render(buffer);
        root.Render(buffer);

        Assert.IsTrue(good.Draws >= 1, "its sibling still drew");
        Assert.IsTrue(bad.Draws >= 2, "the failure is retried on the next frame");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AChildThatThrows_IsRefreshedInsteadOfBreakingTheParent()
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 3) };
        Probe bad = new(Theme()) { ThrowOnDraw = true, ViewSize = new Size(5, 1) };
        Probe good = new(Theme()) { ViewSize = new Size(5, 1) };
        root.AddControl(bad);
        root.AddControl(good);
        root.Draw(new MockBuffer(new Size(10, 3)));
        Assert.AreEqual(1, good.Draws);
    }

    // ---- Children ----

    [Timeout(60_000)]
    [TestMethod]
    public void AddControl_SetsTheParent_NamesTheChild_AndRemoveControlDetachesIt()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        Probe child = new(Theme());
        root.AddControl(child, "sidebar");
        Assert.AreSame(root, child.Parent);
        Assert.AreEqual("sidebar", child.Name);
        Assert.IsTrue(root.Controls.Contains(child));
        root.RemoveControl(child);
        Assert.IsFalse(root.Controls.Contains(child));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Refresh_MarksTheVisibleTreeChanged()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        Probe child = new(Theme()) { ViewSize = new Size(5, 1) };
        root.AddControl(child);
        MockBuffer buffer = new(new Size(10, 3));
        root.Render(buffer);
        int before = child.Draws;
        root.Refresh();
        root.Render(buffer);
        Assert.AreEqual(before + 1, child.Draws);
    }

    // ---- Idle ----

    [Timeout(60_000)]
    [TestMethod]
    public void OnIdle_ReachesTheChildren_AndAFailingOneDoesNotStopTheOthers()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        Probe bad = new(Theme()) { ThrowOnIdle = true };
        Probe good = new(Theme());
        root.AddControl(bad);
        root.AddControl(good);
        root.OnIdle();
        Assert.AreEqual(1, bad.Idles);
        Assert.AreEqual(1, good.Idles);
    }

    // ---- Modality ----

    [Timeout(60_000)]
    [TestMethod]
    public void GetModalChildControl_IsTheLastModalOne_AndNullWithNone()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        Probe first = new(Theme()) { IsModal = true };
        Probe second = new(Theme()) { IsModal = true };
        root.AddControl(first);
        root.AddControl(second);
        Assert.AreSame(second, root.GetModalChildControl());
        second.IsModal = false;
        Assert.AreSame(first, root.GetModalChildControl());
        first.IsModal = false;
        Assert.IsNull(root.GetModalChildControl());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetModalChildControl_PrefersWhatIsNestedInsideAModalControl()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        Probe outer = new(Theme()) { IsModal = true };
        Probe inner = new(Theme()) { IsModal = true };
        outer.AddControl(inner);
        root.AddControl(outer);
        Assert.AreSame(inner, root.GetModalChildControl());
    }

    // ---- Colours ----

    [Timeout(60_000)]
    [TestMethod]
    public void GetColor_GivesEveryVisualStateItsOwnPair()
    {
        Probe probe = new(Theme());
        ColorScheme scheme = new();
        probe.GetColor(scheme, VisualState.Normal, out Color normalFg, out Color normalBg);
        Assert.AreEqual((scheme.Foreground, scheme.Background), (normalFg, normalBg));
        probe.GetColor(scheme, VisualState.Selected, out Color selectedFg, out Color selectedBg);
        Assert.AreEqual((scheme.SelectedForeground, scheme.SelectedBackground), (selectedFg, selectedBg));
        probe.GetColor(scheme, VisualState.Disabled, out Color disabledFg, out Color disabledBg);
        Assert.AreEqual((scheme.DisabledForeground, scheme.DisabledBackground), (disabledFg, disabledBg));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetColor_ASelectedRowOfADisabledControlStaysDisabledLooking()
    {
        Probe probe = new(Theme());
        ColorScheme scheme = new();
        probe.GetColor(scheme, VisualState.SelectedDisabled, out Color foreground, out Color background);
        Assert.AreEqual(scheme.DisabledSelectedForeground, foreground);
        Assert.AreEqual(scheme.DisabledBackground, background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetVisualState_FollowsFocusAndEnabled()
    {
        Window root = new(Theme()) { ViewSize = new Size(10, 3) };
        CheckBox box = new(Theme()) { ViewSize = new Size(5, 1) };
        root.AddControl(box);
        Assert.AreEqual(VisualState.Normal, box.GetVisualState());
        box.SetFocus();
        Assert.AreEqual(VisualState.Focused, box.GetVisualState());
        box.IsEnabled = false;
        Assert.AreEqual(VisualState.Disabled, box.GetVisualState());
    }

    // ---- Pop-ups ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowPopUpWindowAsync_PutsTheWindowOnTheRoot_VisibleAndEnabled()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        Probe nested = new(Theme());
        root.AddControl(nested);
        PopUpWindow window = new("Info", Theme()) { ViewSize = new Size(20, 5) };
        window.IsEnabled = false;

        await nested.ShowPopUpWindowAsync(window);

        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.IsEnabled);
        Assert.AreSame(root, window.Parent);
        Assert.IsTrue(root.Controls.Contains(window));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgressChar_IsOneOfTheFourSpinnerFrames()
    {
        Assert.IsTrue("-\\|/".Contains(Control.ProgressChar()));
    }
}
