using System.Reflection;
using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Compare;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The hex side of a file comparison: how many bytes a row shows, where it opens, and how the cursor moves between the
/// runs of differing bytes. The scroll position is read back from the view, since nothing else shows it.
/// </summary>
[TestClass]
public class BinaryDiffViewTest
{
    private const int Width = 80;   // four bytes a row
    private const int Height = 5;

    private static BinaryDiffView Create() => new(TestTheme()) { ViewSize = new Size(Width, Height) };

    private static long TopOf(BinaryDiffView view) => (long)typeof(BinaryDiffView).GetField("_top", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    /// <summary>Two files of <paramref name="length"/> bytes that differ at each of <paramref name="differAt"/>.</summary>
    private static (byte[] Left, byte[] Right) Files(int length, params int[] differAt)
    {
        byte[] left = new byte[length];
        byte[] right = new byte[length];
        foreach (int index in differAt)
        {
            right[index] = 0xFF;
        }

        return (left, right);
    }

    private static void Load(BinaryDiffView view, byte[] left, byte[] right) =>
        view.SetData(left, right, BinaryComparer.Compare(left, right));

    [Timeout(60_000)]
    [TestMethod]
    public void BytesPerRowGrowsWithTheWidthInPowersOfTwo()
    {
        Assert.AreEqual(4, BinaryDiffView.BytesPerRowFor(80));
        Assert.AreEqual(8, BinaryDiffView.BytesPerRowFor(120));
        Assert.AreEqual(32, BinaryDiffView.BytesPerRowFor(1000), "a row never shows more than 32 bytes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BytesPerRowIsAtLeastFourEvenWhenTheWidthIsTiny()
    {
        Assert.AreEqual(4, BinaryDiffView.BytesPerRowFor(0));
        Assert.AreEqual(4, BinaryDiffView.BytesPerRowFor(10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CurrentDifferenceIsZeroBeforeAnyDataIsLoaded()
    {
        BinaryDiffView view = Create();

        Assert.AreEqual(0, view.CurrentDifference());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetDataOpensAOneRowMarginAboveTheFirstDifference()
    {
        BinaryDiffView view = Create();
        var (left, right) = Files(1000, 40);

        Load(view, left, right);

        // The difference is on row 10 (byte 40 at four bytes a row); the view opens one row above it.
        Assert.AreEqual(9 * 4, TopOf(view));
        Assert.AreEqual(1, view.CurrentDifference());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollToTheLastPageStopsAtTheLastFullPage()
    {
        BinaryDiffView view = Create();
        Load(view, new byte[1000], new byte[1000]);

        view.ScrollTo(int.MaxValue);

        // 1000 bytes make 250 rows; a five-row page starts at row 245.
        Assert.AreEqual(245 * 4, TopOf(view));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollingAboveTheStartOrBelowTheEndIsClamped()
    {
        BinaryDiffView view = Create();
        Load(view, new byte[1000], new byte[1000]);

        view.ScrollBy(-10);
        Assert.AreEqual(0, TopOf(view));

        view.ScrollBy(10_000);
        Assert.AreEqual(245 * 4, TopOf(view));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void JumpToDifferenceMovesToTheNextRunAndThenBackToThePreviousOne()
    {
        BinaryDiffView view = Create();
        var (left, right) = Files(1000, 40, 200);
        Load(view, left, right);

        view.JumpToDifference(next: true);
        // The second run is on row 50, so the view opens one row above it.
        Assert.AreEqual(49 * 4, TopOf(view));
        Assert.AreEqual(2, view.CurrentDifference());

        view.JumpToDifference(next: false);
        Assert.AreEqual(9 * 4, TopOf(view));
        Assert.AreEqual(1, view.CurrentDifference());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void JumpToDifferenceDoesNothingPastTheLastRun()
    {
        BinaryDiffView view = Create();
        var (left, right) = Files(1000, 40);
        Load(view, left, right);
        long before = TopOf(view);

        view.JumpToDifference(next: true);

        Assert.AreEqual(before, TopOf(view));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IdenticalFilesHaveNoDifferenceToJumpTo()
    {
        BinaryDiffView view = Create();
        Load(view, new byte[100], new byte[100]);

        view.JumpToDifference(next: true);
        view.JumpToDifference(next: false);

        Assert.AreEqual(0, TopOf(view));
        Assert.AreEqual(0, view.CurrentDifference());
    }
}
