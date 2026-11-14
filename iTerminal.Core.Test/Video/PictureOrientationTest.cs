using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class PictureOrientationTest
{
    private static readonly byte[] Red = [255, 0, 0];
    private static readonly byte[] Blue = [0, 0, 255];

    /// <summary>A 2 × 1 picture: red on the left, blue on the right.</summary>
    private static byte[] RedThenBlue() => [.. Red, .. Blue];

    private static byte[] Pixel(byte[] rgb, int x, int y, int width)
    {
        int at = (y * width + x) * 3;
        return rgb[at..(at + 3)];
    }

    [TestMethod]
    public void RotateClockwise_MovesTheLeftEdgeToTheTop()
    {
        byte[] turned = PictureOrientation.None.RotateClockwise().Apply(RedThenBlue(), 2, 1);

        CollectionAssert.AreEqual(Red, Pixel(turned, 0, 0, 1), "red is on top");
        CollectionAssert.AreEqual(Blue, Pixel(turned, 0, 1, 1), "blue is at the bottom");
    }

    [TestMethod]
    public void RotateCounterclockwise_MovesTheLeftEdgeToTheBottom()
    {
        byte[] turned = PictureOrientation.None.RotateCounterclockwise().Apply(RedThenBlue(), 2, 1);

        CollectionAssert.AreEqual(Blue, Pixel(turned, 0, 0, 1), "blue is on top");
        CollectionAssert.AreEqual(Red, Pixel(turned, 0, 1, 1), "red is at the bottom");
    }

    [TestMethod]
    public void MirrorHorizontal_SwapsTheSidesWithoutChangingTheShape()
    {
        var orientation = PictureOrientation.None.MirrorHorizontal();

        byte[] mirrored = orientation.Apply(RedThenBlue(), 2, 1);

        CollectionAssert.AreEqual(Blue, Pixel(mirrored, 0, 0, 2));
        CollectionAssert.AreEqual(Red, Pixel(mirrored, 1, 0, 2));
        Assert.IsFalse(orientation.SwapsAxes);
    }

    [TestMethod]
    public void MirrorVertical_SwapsTheTopAndBottom()
    {
        byte[] rgb = [.. Red, .. Blue];

        byte[] mirrored = PictureOrientation.None.MirrorVertical().Apply(rgb, 1, 2);

        CollectionAssert.AreEqual(Blue, Pixel(mirrored, 0, 0, 1));
        CollectionAssert.AreEqual(Red, Pixel(mirrored, 0, 1, 1));
    }

    [TestMethod]
    public void FourQuarterTurns_ReturnToTheOriginalPicture()
    {
        PictureOrientation orientation = PictureOrientation.None;
        byte[] rgb = RedThenBlue();
        int width = 2;
        int height = 1;
        for (int turn = 0; turn < 4; turn++)
        {
            rgb = orientation.RotateClockwise().Apply(rgb, width, height);
            (width, height) = (height, width);
            orientation = orientation.RotateClockwise();
        }

        CollectionAssert.AreEqual(RedThenBlue(), rgb);
        Assert.AreEqual(2, width);
        Assert.AreEqual(1, height);
    }

    [TestMethod]
    public void SwapsAxes_IsTrueOnlyForAQuarterTurn()
    {
        Assert.IsFalse(PictureOrientation.None.SwapsAxes);
        Assert.IsTrue(PictureOrientation.None.RotateClockwise().SwapsAxes);
        Assert.IsFalse(PictureOrientation.None.RotateClockwise().RotateClockwise().SwapsAxes);
        Assert.IsTrue(PictureOrientation.None.RotateCounterclockwise().SwapsAxes);
    }

    [TestMethod]
    public void Apply_ReturnsTheSameArrayWhenNothingIsTurned()
    {
        byte[] rgb = RedThenBlue();

        Assert.AreSame(rgb, PictureOrientation.None.Apply(rgb, 2, 1));
    }
}
