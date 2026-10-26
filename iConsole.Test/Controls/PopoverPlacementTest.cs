using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Test.Controls;

[TestClass]
public class PopoverPlacementTest
{
    private static readonly Size Screen = new(80, 25);

    [TestMethod]
    public void OpensBelowTheTargetWhenItFits()
    {
        Position position = PopoverPlacement.Place(new Rectangle(10, 5, 8, 1), new Size(20, 6), Screen, PopoverSide.Below);

        Assert.AreEqual(new Position(10, 6), position);
    }

    [TestMethod]
    public void TurnsAboveTheTargetWhenBelowIsTooTight()
    {
        Position position = PopoverPlacement.Place(new Rectangle(10, 22, 8, 1), new Size(20, 6), Screen, PopoverSide.Below);

        Assert.AreEqual(new Position(10, 16), position);
    }

    [TestMethod]
    public void TurnsBelowWhenAboveWasPreferredAndIsTooTight()
    {
        Position position = PopoverPlacement.Place(new Rectangle(10, 2, 8, 1), new Size(20, 6), Screen, PopoverSide.Above);

        Assert.AreEqual(new Position(10, 3), position);
    }

    [TestMethod]
    public void TurnsLeftOfTheTargetWhenRightIsTooTight()
    {
        Position position = PopoverPlacement.Place(new Rectangle(60, 5, 15, 1), new Size(20, 4), Screen, PopoverSide.Right);

        Assert.AreEqual(new Position(40, 5), position);
    }

    [TestMethod]
    public void IsPulledBackOnScreenAlongTheOtherAxis()
    {
        Position position = PopoverPlacement.Place(new Rectangle(70, 5, 8, 1), new Size(20, 6), Screen, PopoverSide.Below);

        Assert.AreEqual(new Position(60, 6), position);
    }

    [TestMethod]
    public void StaysOnScreenWhenNeitherSideFits()
    {
        Position position = PopoverPlacement.Place(new Rectangle(10, 14, 8, 1), new Size(20, 20), Screen, PopoverSide.Below);

        Assert.IsTrue(position.Y >= 0 && position.Y + 20 <= 25);
    }

    [TestMethod]
    public void APointOpensWithItsCornerOnThePoint()
    {
        Assert.AreEqual(new Position(12, 7), PopoverPlacement.PlaceAtPoint(new Position(12, 7), new Size(20, 6), Screen));
    }

    [TestMethod]
    public void APointNearTheEdgesOpensTowardsTheMiddle()
    {
        Position position = PopoverPlacement.PlaceAtPoint(new Position(75, 22), new Size(20, 6), Screen);

        Assert.AreEqual(new Position(55, 16), position);
    }

    [TestMethod]
    public void APopoverLargerThanTheScreenIsPlacedAtTheOrigin()
    {
        Assert.AreEqual(new Position(0, 0), PopoverPlacement.PlaceAtPoint(new Position(5, 5), new Size(100, 40), Screen));
    }
}
