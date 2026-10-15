using iConsole.Display;
using iConsole.Test.Core.Automation;

namespace iConsole.Test.Automation;

[TestClass]
public class VirtualConsoleDriverTest
{
    private static ConxelString Text(string text, Color foreground = Color.White, Color background = Color.Black) =>
        new(text, foreground, background);

    [TestMethod]
    public void StartsBlankAtTheRequestedSize()
    {
        var console = new VirtualConsoleDriver(30, 5);

        var screen = console.Snapshot();

        Assert.AreEqual(30, screen.Width);
        Assert.AreEqual(5, screen.Height);
        Assert.IsTrue(screen.IsBlank);
        Assert.AreEqual(30, console.WindowSize.Width);
    }

    [TestMethod]
    public void WritesLandWhereTheyAreAimedWithTheirColours()
    {
        var console = new VirtualConsoleDriver(20, 3);

        console.Write(4, 1, Text("hello", Color.Yellow, Color.Blue));

        var screen = console.Snapshot();
        Assert.AreEqual("    hello", screen.Lines[1]);
        Assert.AreEqual(Color.Yellow, screen.CellOf("hello").ForegroundColor);
        Assert.AreEqual(Color.Blue, screen.BackgroundAt(8, 1));
    }

    [TestMethod]
    public void WritesAreClippedToTheWindow()
    {
        var console = new VirtualConsoleDriver(5, 2);

        console.Write(3, 0, Text("abcdef"));
        console.Write(0, 7, Text("off"));
        console.Write(-1, 1, new Conxel('x', Color.White, Color.Black));

        Assert.AreEqual("   ab", console.Snapshot().Lines[0]);
        Assert.AreEqual(string.Empty, console.Snapshot().Lines[1]);
    }

    [TestMethod]
    public void ASingleCellWriteLands()
    {
        var console = new VirtualConsoleDriver(5, 2);

        console.Write(2, 1, new Conxel('#', Color.Red, Color.Black));

        Assert.AreEqual('#', console.Snapshot().CharAt(2, 1));
    }

    [TestMethod]
    public void ClearBlanksTheScreen()
    {
        var console = new VirtualConsoleDriver(10, 2);
        console.Write(0, 0, Text("text"));

        console.Clear();

        Assert.IsTrue(console.Snapshot().IsBlank);
    }

    [TestMethod]
    public void ASnapshotDoesNotChangeAfterItIsTaken()
    {
        var console = new VirtualConsoleDriver(10, 2);
        console.Write(0, 0, Text("before"));
        var before = console.Snapshot();

        console.Write(0, 0, Text("after!"));

        Assert.AreEqual("before", before.Lines[0]);
        Assert.IsFalse(before.HasSameContent(console.Snapshot()));
        Assert.IsTrue(before.Version < console.Version);
    }

    [TestMethod]
    public void AResizeTakesEffectWhenTheSizeIsNextPolled()
    {
        var console = new VirtualConsoleDriver(10, 3);
        console.Write(0, 0, Text("keep"));

        console.Resize(20, 4);
        Assert.AreEqual(10, console.WindowSize.Width, "a real terminal reports the new size when asked");

        console.RefreshWindowSize();
        var screen = console.Snapshot();
        Assert.AreEqual(20, screen.Width);
        Assert.AreEqual(4, screen.Height);
        Assert.AreEqual("keep", screen.Lines[0]);
    }

    [TestMethod]
    public void BeepsAreCounted()
    {
        var console = new VirtualConsoleDriver();

        console.Beep();
        console.Beep();

        Assert.AreEqual(2, console.BeepCount);
    }

    [TestMethod]
    public void TheCursorIsKeptInsideTheWindow()
    {
        var console = new VirtualConsoleDriver(10, 5);

        console.SetCursorPosition(50, -3);

        Assert.AreEqual(9, console.GetCursorPosition().X);
        Assert.AreEqual(0, console.GetCursorPosition().Y);
    }

    [TestMethod]
    public void SnapshotsFindTextAndRegions()
    {
        var console = new VirtualConsoleDriver(20, 3);
        console.Write(0, 0, Text("ab ab"));
        console.Write(2, 2, Text("panel"));

        var screen = console.Snapshot();

        Assert.AreEqual(2, screen.FindAll("ab").Count);
        Assert.AreEqual(2, screen.RowOf("panel"));
        Assert.AreEqual(-1, screen.RowOf("missing"));
        CollectionAssert.AreEqual(new[] { "", "nel" }, screen.Region(4, 1, 3, 2).ToArray());
        Assert.AreEqual("ab", screen.Text(3, 0, 2));
        StringAssert.Contains(screen.Dump(), "02|  panel");
    }

    [TestMethod]
    public void CellOfFailsForTextThatIsNotThere()
    {
        var screen = new VirtualConsoleDriver(5, 1).Snapshot();

        Assert.ThrowsExactly<AssertFailedException>(() => screen.CellOf("x"));
    }

    [TestMethod]
    public void SizesBelowOneAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VirtualConsoleDriver(0, 5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new VirtualConsoleDriver(5, 5).Resize(5, 0));
    }
}
