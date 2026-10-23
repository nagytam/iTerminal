using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Tab and Shift+Tab: which control gets the keyboard next.</summary>
[TestClass]
public class FocusTraversalTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static CheckBox Box(string name) => new(Theme()) { Name = name, Label = name, ViewSize = new Size(10, 1) };

    private static string? Focused(Control root) => (root.GetFocusedControl() as Control)?.Name;

    private static void Forward(Control root) => root.OnHandleTabForward();

    private static void Backward(Control root) => root.OnHandleTabBackward();

    [TestMethod]
    public void Tab_VisitsSiblingsInOrder_AndWrapsAround()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        CheckBox b = Box("b");
        CheckBox c = Box("c");
        root.AddControl(a);
        root.AddControl(b);
        root.AddControl(c);
        a.SetFocus();

        Forward(root);
        Assert.AreEqual("b", Focused(root));
        Forward(root);
        Assert.AreEqual("c", Focused(root));
        Forward(root);
        Assert.AreEqual("a", Focused(root));
    }

    [TestMethod]
    public void ShiftTab_VisitsSiblingsBackwards_AndWrapsAround()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        CheckBox b = Box("b");
        CheckBox c = Box("c");
        root.AddControl(a);
        root.AddControl(b);
        root.AddControl(c);
        a.SetFocus();

        Backward(root);
        Assert.AreEqual("c", Focused(root));
        Backward(root);
        Assert.AreEqual("b", Focused(root));
        Backward(root);
        Assert.AreEqual("a", Focused(root));
    }

    [TestMethod]
    public void Tab_GoesInsideAContainerThatCannotTakeTheFocusItself()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        GroupBox group = new(Theme()) { Name = "group", ViewSize = new Size(20, 4) };
        CheckBox inside1 = Box("inside1");
        CheckBox inside2 = Box("inside2");
        group.AddControl(inside1);
        group.AddControl(inside2);
        CheckBox d = Box("d");
        root.AddControl(a);
        root.AddControl(group);
        root.AddControl(d);
        a.SetFocus();

        Forward(root);
        Assert.AreEqual("inside1", Focused(root));
        Forward(root);
        Assert.AreEqual("inside2", Focused(root));
        Forward(root);
        Assert.AreEqual("d", Focused(root));
        Forward(root);
        Assert.AreEqual("a", Focused(root));
    }

    [TestMethod]
    public void ShiftTab_GoesBackIntoAContainer()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        GroupBox group = new(Theme()) { Name = "group", ViewSize = new Size(20, 4) };
        CheckBox inside1 = Box("inside1");
        CheckBox inside2 = Box("inside2");
        group.AddControl(inside1);
        group.AddControl(inside2);
        CheckBox d = Box("d");
        root.AddControl(group);
        root.AddControl(d);
        d.SetFocus();

        Backward(root);
        Assert.AreEqual("inside2", Focused(root));
        Backward(root);
        Assert.AreEqual("inside1", Focused(root));
        Backward(root);
        Assert.AreEqual("d", Focused(root));
    }

    [TestMethod]
    public void Tab_SkipsDisabledControls()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        CheckBox b = Box("b");
        b.IsEnabled = false;
        CheckBox c = Box("c");
        root.AddControl(a);
        root.AddControl(b);
        root.AddControl(c);
        a.SetFocus();

        Forward(root);
        Assert.AreEqual("c", Focused(root));
        Backward(root);
        Assert.AreEqual("a", Focused(root));
    }

    [TestMethod]
    public void Tab_SkipsHiddenControls()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        CheckBox b = Box("b");
        CheckBox c = Box("c");
        root.AddControl(a);
        root.AddControl(b);
        root.AddControl(c);
        b.IsVisible = false;
        a.SetFocus();

        Forward(root);
        Assert.AreEqual("c", Focused(root));
    }

    [TestMethod]
    public void Tab_WithASingleFocusableControl_StaysOnIt()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox only = Box("only");
        root.AddControl(only);
        only.SetFocus();

        Forward(root);
        Assert.AreEqual("only", Focused(root));
        Backward(root);
        Assert.AreEqual("only", Focused(root));
    }

    [TestMethod]
    public void Tab_WithNothingFocused_StartsAtTheFirstControl_AndShiftTabAtTheLast()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        root.AddControl(Box("a"));
        root.AddControl(Box("b"));
        Forward(root);
        Assert.AreEqual("a", Focused(root));

        Window other = new(Theme()) { ViewSize = new Size(40, 10) };
        other.AddControl(Box("a"));
        other.AddControl(Box("b"));
        Backward(other);
        Assert.AreEqual("b", Focused(other));
    }

    [TestMethod]
    public void Tab_WithNothingToFocus_DoesNothingAndDoesNotThrow()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        Label label = new("text", Theme());
        root.AddControl(label);
        CheckBox disabled = Box("off");
        disabled.IsEnabled = false;
        root.AddControl(disabled);
        Forward(root);
        Backward(root);
        Assert.IsFalse(disabled.IsFocused);
    }

    [TestMethod]
    public void Tab_StopsAtAControlThatTakesTheFocusItself_NotAtWhatItHolds()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        CheckBox holder = Box("holder");
        holder.AddControl(Box("held"));
        root.AddControl(a);
        root.AddControl(holder);
        a.SetFocus();
        Forward(root);
        Assert.AreEqual("holder", Focused(root));
        Forward(root);
        Assert.AreEqual("a", Focused(root));
    }

    [TestMethod]
    public void GetTabStops_ListsTheVisibleEnabledFocusableControlsInOrder()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        GroupBox group = new(Theme()) { ViewSize = new Size(20, 4) };
        group.AddControl(Box("g1"));
        CheckBox hiddenInside = Box("g2");
        group.AddControl(hiddenInside);
        hiddenInside.IsVisible = false;
        root.AddControl(Box("a"));
        root.AddControl(group);
        CheckBox off = Box("off");
        off.IsEnabled = false;
        root.AddControl(off);
        root.AddControl(Box("z"));
        CollectionAssert.AreEqual(new[] { "a", "g1", "z" }, root.GetTabStops().Select(c => ((Control)c).Name).ToArray());
    }

    [TestMethod]
    public void AHiddenContainer_HidesWhatIsInside()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        GroupBox group = new(Theme()) { ViewSize = new Size(20, 4) };
        group.AddControl(Box("inner"));
        root.AddControl(Box("a"));
        root.AddControl(group);
        group.IsVisible = false;
        CollectionAssert.AreEqual(new[] { "a" }, root.GetTabStops().Select(c => ((Control)c).Name).ToArray());
    }

    [TestMethod]
    public void Tab_LeavesExactlyOneControlFocused()
    {
        Window root = new(Theme()) { ViewSize = new Size(40, 10) };
        CheckBox a = Box("a");
        CheckBox b = Box("b");
        CheckBox c = Box("c");
        root.AddControl(a);
        root.AddControl(b);
        root.AddControl(c);
        a.SetFocus();
        for (int i = 0; i < 7; i++)
        {
            Forward(root);
            Assert.AreEqual(1, new[] { a, b, c }.Count(x => x.IsFocused));
        }
    }
}
