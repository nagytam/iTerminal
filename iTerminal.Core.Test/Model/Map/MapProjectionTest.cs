using iTerminal.Core.Model.Map;

namespace iTerminal.Core.Test.Model.Map;

[TestClass]
public class MapProjectionTest
{
    [TestMethod]
    public void BudapestIsInTheKnownTileAtZoom12()
    {
        var (x, y) = MapProjection.ToPixel(47.4979, 19.0402, 12);

        Assert.AreEqual(2264, (int)(x / MapProjection.TileSize));
        Assert.AreEqual(1432, (int)(y / MapProjection.TileSize));
    }

    [TestMethod]
    public void PixelsRoundTripBackToCoordinates()
    {
        var (x, y) = MapProjection.ToPixel(-33.8688, 151.2093, 15);
        var (latitude, longitude) = MapProjection.FromPixel(x, y, 15);

        Assert.AreEqual(-33.8688, latitude, 1e-6);
        Assert.AreEqual(151.2093, longitude, 1e-6);
    }

    [TestMethod]
    public void TheWorldCentreIsTheMiddleOfTheWorld()
    {
        var (x, y) = MapProjection.ToPixel(0, 0, 3);

        Assert.AreEqual(MapProjection.WorldSize(3) / 2, x, 1e-6);
        Assert.AreEqual(MapProjection.WorldSize(3) / 2, y, 1e-6);
    }

    [TestMethod]
    public void LongitudeWrapsAroundTheDateLine()
    {
        Assert.AreEqual(-170, MapProjection.WrapLongitude(190), 1e-9);
        Assert.AreEqual(170, MapProjection.WrapLongitude(-190), 1e-9);
        Assert.AreEqual(-180, MapProjection.WrapLongitude(180), 1e-9);
    }

    [TestMethod]
    public void LatitudeIsClampedToTheMercatorLimit()
    {
        Assert.AreEqual(MapProjection.MaxLatitude, MapProjection.ClampLatitude(90));
        Assert.AreEqual(-MapProjection.MaxLatitude, MapProjection.ClampLatitude(-89));
    }

    [TestMethod]
    public void MetersPerPixelHalvesWithEachZoomAndShrinksTowardThePoles()
    {
        Assert.AreEqual(156543.03, MapProjection.MetersPerPixel(0, 0), 0.01);
        Assert.AreEqual(MapProjection.MetersPerPixel(0, 5) / 2, MapProjection.MetersPerPixel(0, 6), 1e-9);
        Assert.IsTrue(MapProjection.MetersPerPixel(60, 5) < MapProjection.MetersPerPixel(0, 5));
    }

    [TestMethod]
    public void NiceDistanceRoundsDownToOneTwoOrFive()
    {
        Assert.AreEqual(5000, MapProjection.NiceDistance(7300));
        Assert.AreEqual(2000, MapProjection.NiceDistance(2999));
        Assert.AreEqual(100, MapProjection.NiceDistance(180));
        Assert.AreEqual("1.5 km", MapProjection.FormatDistance(1500));
        Assert.AreEqual("350 m", MapProjection.FormatDistance(350));
    }

    [TestMethod]
    [DataRow("47.4979, 19.0402", 47.4979, 19.0402)]
    [DataRow("47.4979 19.0402", 47.4979, 19.0402)]
    [DataRow("47.4979N 19.0402E", 47.4979, 19.0402)]
    [DataRow("33.9S, 151.2E", -33.9, 151.2)]
    [DataRow("-12.5, -77.0", -12.5, -77.0)]
    public void ParsesCoordinates(string text, double latitude, double longitude)
    {
        Assert.IsTrue(MapProjection.TryParseCoordinates(text, out var parsedLatitude, out var parsedLongitude));
        Assert.AreEqual(latitude, parsedLatitude, 1e-9);
        Assert.AreEqual(longitude, parsedLongitude, 1e-9);
    }

    [TestMethod]
    [DataRow("Budapest")]
    [DataRow("91, 10")]
    [DataRow("10, 181")]
    [DataRow("10")]
    [DataRow("")]
    public void RejectsWhatIsNotACoordinate(string text) =>
        Assert.IsFalse(MapProjection.TryParseCoordinates(text, out _, out _));

    [TestMethod]
    public void FormatsCoordinatesWithHemispheres() =>
        Assert.AreEqual("33.9000°S 70.5000°W", MapProjection.FormatCoordinates(-33.9, -70.5));

    [TestMethod]
    public void ATileKnowsItsParent() =>
        Assert.AreEqual(new MapTileKey(11, 1132, 716), new MapTileKey(12, 2264, 1432).Parent);
}
