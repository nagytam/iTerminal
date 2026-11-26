using iConsole.Controls;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Test.Model;

[TestClass]
public class StudioPanelLayoutTest
{
    [TestMethod]
    public void TheDefaultArrangementHasTheEditorInTheCentreAndTheOtherPanelsOnTheEdges()
    {
        var layout = StudioPanelLayout.Default();

        Assert.AreEqual(DockSide.Fill, layout.SideOf(StudioPanelKind.Editor));
        Assert.AreEqual(DockSide.Right, layout.SideOf(StudioPanelKind.Explorer));
        Assert.AreEqual(DockSide.Bottom, layout.SideOf(StudioPanelKind.Output));
        Assert.AreEqual(DockSide.Top, layout.SideOf(StudioPanelKind.Errors));
        Assert.IsNull(layout.At(DockSide.Left), "the left edge is free by default");
    }

    [TestMethod]
    public void DockedListsTheTopBottomAndSideEdgesBeforeTheCentre()
    {
        var docked = StudioPanelLayout.Default().Docked().Select(panel => panel.Kind).ToList();

        CollectionAssert.AreEqual(new[] { StudioPanelKind.Errors, StudioPanelKind.Output, StudioPanelKind.Explorer, StudioPanelKind.Editor }, docked);
    }

    [TestMethod]
    public void MovingAPanelOntoAnEmptyEdgeMovesIt()
    {
        var layout = StudioPanelLayout.Default();

        Assert.AreEqual(StudioPanelMove.Moved, layout.Move(StudioPanelKind.Explorer, DockSide.Left));

        Assert.AreEqual(DockSide.Left, layout.SideOf(StudioPanelKind.Explorer));
        Assert.IsNull(layout.At(DockSide.Right), "the edge it left is now empty");
    }

    [TestMethod]
    public void MovingAPanelOntoATakenSideSwapsItWithThePanelThere()
    {
        var layout = StudioPanelLayout.Default();

        Assert.AreEqual(StudioPanelMove.Swapped, layout.Move(StudioPanelKind.Explorer, DockSide.Top));

        Assert.AreEqual(DockSide.Top, layout.SideOf(StudioPanelKind.Explorer));
        Assert.AreEqual(DockSide.Right, layout.SideOf(StudioPanelKind.Errors));
    }

    [TestMethod]
    public void TheCentrePanelCannotMoveToAnEmptyEdgeSoTheCentreIsNeverEmpty()
    {
        var layout = StudioPanelLayout.Default();

        Assert.AreEqual(StudioPanelMove.Refused, layout.Move(StudioPanelKind.Editor, DockSide.Left));

        Assert.AreEqual(DockSide.Fill, layout.SideOf(StudioPanelKind.Editor));
        Assert.IsNull(layout.At(DockSide.Left));
    }

    [TestMethod]
    public void TheCentrePanelSwapsWithThePanelOnAnEdge()
    {
        var layout = StudioPanelLayout.Default();

        Assert.AreEqual(StudioPanelMove.Swapped, layout.Move(StudioPanelKind.Editor, DockSide.Top));

        Assert.AreEqual(DockSide.Top, layout.SideOf(StudioPanelKind.Editor));
        Assert.AreEqual(StudioPanelKind.Errors, layout.At(DockSide.Fill), "the Error List took the centre");
        Assert.AreEqual(StudioPanelMove.Swapped, layout.Move(StudioPanelKind.Editor, DockSide.Fill), "and swaps back");
        Assert.AreEqual(DockSide.Fill, layout.SideOf(StudioPanelKind.Editor));
    }

    [TestMethod]
    public void MovingToTheSideAPanelIsAlreadyOnChangesNothing()
    {
        var layout = StudioPanelLayout.Default();

        Assert.AreEqual(StudioPanelMove.Unchanged, layout.Move(StudioPanelKind.Output, DockSide.Bottom));
    }

    [TestMethod]
    public void APanelMovedOutOfTheCentreTakesADefaultShareOfItsEdge()
    {
        var layout = StudioPanelLayout.Default();
        layout.Move(StudioPanelKind.Editor, DockSide.Right);

        Assert.AreEqual(DockSide.Right, layout.SideOf(StudioPanelKind.Editor));
        Assert.AreEqual(25, layout.PercentOf(StudioPanelKind.Editor), "the centre keeps no share, so the edge starts at 25%");
        Assert.AreEqual(34, layout.PercentOf(StudioPanelKind.Explorer), "the panel it swapped with keeps its share");
    }

    [TestMethod]
    public void ResizeChangesAnEdgePanelsShare()
    {
        var layout = StudioPanelLayout.Default();

        Assert.IsTrue(layout.Resize(StudioPanelKind.Explorer, 5));

        Assert.AreEqual(39, layout.PercentOf(StudioPanelKind.Explorer));
    }

    [TestMethod]
    public void ResizeStopsAtTheMinimumAndReportsWhenNothingChanged()
    {
        var layout = StudioPanelLayout.Default();

        Assert.IsTrue(layout.Resize(StudioPanelKind.Errors, -100));
        Assert.AreEqual(StudioPanelLayout.MinPercent, layout.PercentOf(StudioPanelKind.Errors));
        Assert.IsFalse(layout.Resize(StudioPanelKind.Errors, -5), "already at the minimum");
    }

    [TestMethod]
    public void ResizeStopsAtTheMaximum()
    {
        var layout = StudioPanelLayout.Default();

        Assert.IsTrue(layout.Resize(StudioPanelKind.Output, 1000));

        Assert.AreEqual(StudioPanelLayout.MaxPercent, layout.PercentOf(StudioPanelKind.Output));
    }

    [TestMethod]
    public void TheCentrePanelHasNoShareToResize()
    {
        var layout = StudioPanelLayout.Default();

        Assert.IsFalse(layout.Resize(StudioPanelKind.Editor, 5));
    }
}
