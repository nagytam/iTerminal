using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class OrientationTest
{
    [TestMethod]
    public void Orientation_Horizontal_HasValue()
    {
        var orientation = Orientation.Horizontal;
        Assert.AreEqual(0, (int)orientation);
    }

    [TestMethod]
    public void Orientation_Vertical_HasValue()
    {
        var orientation = Orientation.Vertical;
        Assert.AreEqual(1, (int)orientation);
    }

    [TestMethod]
    public void Orientation_AllValuesAreUnique()
    {
        var orientations = new[]
        {
            Orientation.Horizontal,
            Orientation.Vertical
        };

        var distinctOrientations = orientations.Distinct().ToArray();
        Assert.AreEqual(orientations.Length, distinctOrientations.Length);
    }

    [TestMethod]
    public void Orientation_CanCompare()
    {
        var orientation1 = Orientation.Horizontal;
        var orientation2 = Orientation.Horizontal;
        var orientation3 = Orientation.Vertical;

        Assert.AreEqual(orientation1, orientation2);
        Assert.AreNotEqual(orientation1, orientation3);
    }
}
