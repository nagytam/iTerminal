using iConsole.Controls;

namespace iConsole.Test.Controls;

[TestClass]
public class AnalogClockTest
{
    private const int Width = 31;
    private const int Height = 15;

    private static IReadOnlyList<string> Face(TimeSpan time, bool seconds = false) => AnalogClock.Render(Width, Height, time, seconds);

    [TestMethod]
    public void TheCentreIsMarkedAndTheFaceIsTheRequestedSize()
    {
        var rows = Face(TimeSpan.Zero);

        Assert.AreEqual(Height, rows.Count);
        Assert.IsTrue(rows.All(row => row.Length == Width));
        Assert.AreEqual('•', rows[Height / 2][Width / 2]);
    }

    [TestMethod]
    public void AtTwelveTheHandsPointUp()
    {
        var rows = Face(TimeSpan.Zero);

        // The minute hand runs up the centre column from the middle of the face.
        Assert.IsTrue(rows.Take(Height / 2).Any(row => row[Width / 2] == '|'), "a vertical hand above the centre");
    }

    [TestMethod]
    public void AtThreeTheHourHandPointsRight()
    {
        var rows = Face(TimeSpan.FromHours(3));

        // The hour hand is drawn '#' out to the right of the centre, on the centre row.
        string centreRow = rows[Height / 2];
        int hashes = centreRow.Count(c => c == '#');
        Assert.IsTrue(hashes >= 3, $"expected a hour hand on the centre row: '{centreRow}'");
        Assert.IsTrue(centreRow.IndexOf('#') > Width / 2, "the hour hand points right");
    }

    [TestMethod]
    public void ATimeAfterMidnightWrapsToTheSameFace()
    {
        CollectionAssert.AreEqual(Face(TimeSpan.FromHours(3)).ToArray(), Face(TimeSpan.FromHours(27)).ToArray(), "03:00 and 27:00 show the same face");
    }

    [TestMethod]
    public void ASecondHandIsDrawnOnlyWhenAsked()
    {
        // At 15 seconds the second hand points right and is drawn with '.', which the ticks (·) do not use.
        var time = TimeSpan.FromSeconds(15);
        Assert.IsFalse(Face(time, seconds: false).Any(row => row.Contains('.')));
        Assert.IsTrue(Face(time, seconds: true).Any(row => row.Contains('.')));
    }

    [TestMethod]
    public void ATooSmallFaceDrawsNoHands()
    {
        var rows = AnalogClock.Render(1, 1, TimeSpan.FromHours(3), true);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(" ", rows[0]);
    }
}
