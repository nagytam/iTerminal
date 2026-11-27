using iConsole.Controls;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Test.Model;

/// <summary>The stored form of the panel arrangement: what is written reads back as the same arrangement, and a damaged one gives the default.</summary>
[TestClass]
public class StudioPanelLayoutStorageTest
{
    [TestMethod]
    public void AnArrangementReadsBackAsItWasWritten()
    {
        var layout = StudioPanelLayout.Default();
        layout.Move(StudioPanelKind.Explorer, DockSide.Left);
        layout.Resize(StudioPanelKind.Explorer, 10);
        layout.Resize(StudioPanelKind.Output, -8);

        var restored = StudioPanelLayout.Parse(layout.Serialize());

        Assert.AreEqual(layout.Serialize(), restored.Serialize());
        Assert.AreEqual(DockSide.Left, restored.SideOf(StudioPanelKind.Explorer));
        Assert.AreEqual(layout.PercentOf(StudioPanelKind.Explorer), restored.PercentOf(StudioPanelKind.Explorer));
        Assert.AreEqual(layout.PercentOf(StudioPanelKind.Output), restored.PercentOf(StudioPanelKind.Output));
        Assert.AreEqual(DockSide.Fill, restored.SideOf(StudioPanelKind.Editor));
    }

    [TestMethod]
    public void AMissingOrEmptyStoredArrangementGivesTheDefault()
    {
        Assert.AreEqual(StudioPanelLayout.Default().Serialize(), StudioPanelLayout.Parse(null).Serialize());
        Assert.AreEqual(StudioPanelLayout.Default().Serialize(), StudioPanelLayout.Parse("  ").Serialize());
    }

    [TestMethod]
    public void ADamagedArrangementGivesTheDefault()
    {
        string defaultText = StudioPanelLayout.Default().Serialize();

        Assert.AreEqual(defaultText, StudioPanelLayout.Parse("not a layout").Serialize());
        Assert.AreEqual(defaultText, StudioPanelLayout.Parse("Editor:Fill:0;Explorer:Right:34").Serialize(), "a panel is missing");
        Assert.AreEqual(defaultText, StudioPanelLayout.Parse("Editor:Fill:0;Explorer:Right:34;Output:Right:33").Serialize(), "two panels share an edge");
        Assert.AreEqual(defaultText, StudioPanelLayout.Parse("Editor:Left:0;Explorer:Right:34;Output:Bottom:33").Serialize(), "the centre is empty");
        Assert.AreEqual(defaultText, StudioPanelLayout.Parse("Editor:Fill:0;Explorer:Diagonal:34;Output:Bottom:33").Serialize(), "an unknown side");
        Assert.AreEqual(defaultText, StudioPanelLayout.Parse("Editor:Fill:0;Explorer:Right:x;Output:Bottom:33").Serialize(), "a share that is not a number");
    }

    [TestMethod]
    public void AStoredShareOutsideTheLimitsIsKeptWithinThem()
    {
        var layout = StudioPanelLayout.Parse("Editor:Fill:0;Explorer:Right:99;Output:Bottom:1");

        Assert.AreEqual(StudioPanelLayout.MaxPercent, layout.PercentOf(StudioPanelKind.Explorer));
        Assert.AreEqual(StudioPanelLayout.MinPercent, layout.PercentOf(StudioPanelKind.Output));
    }
}
