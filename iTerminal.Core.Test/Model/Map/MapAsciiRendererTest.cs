using iConsole.Display;
using iTerminal.Core.Model.Map;
using SkiaSharp;

namespace iTerminal.Core.Test.Model.Map;

[TestClass]
public class MapAsciiRendererTest
{
    private static MapTile Tile(byte[] png) => MapTile.Decode(png)!;

    private static MapCell Cell(MapTile tile, double u = 64, double v = 64) =>
        MapAsciiRenderer.Render(tile, u, v, MapAsciiRenderer.CellWidth, MapAsciiRenderer.CellHeight);

    [Timeout(60_000)]
    [TestMethod]
    public void WaterIsDrawnAsBlueWaves()
    {
        var cell = Cell(Tile(MapTestSupport.Png(MapTestSupport.Water)));

        Assert.AreEqual('~', cell.Char);
        Assert.AreEqual(Color.Blue, cell.Foreground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GreenSpaceIsDrawnAsGreenQuotes()
    {
        var cell = Cell(Tile(MapTestSupport.Png(MapTestSupport.Park)));

        Assert.AreEqual('"', cell.Char);
        Assert.AreEqual(Color.Green, cell.Foreground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EmptyLandIsBlank() =>
        Assert.AreEqual(' ', Cell(Tile(MapTestSupport.Png(MapTestSupport.Land))).Char);

    [Timeout(60_000)]
    [TestMethod]
    public void AStrokeAcrossTheBlockIsDrawnWithDensity()
    {
        var cell = Cell(Tile(MapTestSupport.RoadPng()), 64, 124);

        Assert.AreNotEqual(' ', cell.Char);
        Assert.IsTrue(MapAsciiRenderer.Ramp.Contains(cell.Char), "the character is not on the density ramp");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WarmRoadsAreYellow()
    {
        var cell = Cell(Tile(MapTestSupport.Png(new SKColor(252, 214, 164))));

        Assert.AreEqual(Color.Yellow, cell.Foreground);
        Assert.AreNotEqual(' ', cell.Char);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABlockAtTheEdgeOfTheTileDoesNotThrow() =>
        Assert.AreEqual(' ', MapAsciiRenderer.Render(Tile(MapTestSupport.Png(MapTestSupport.Land)), 254, 254, 4, 8).Char);

    [Timeout(60_000)]
    [TestMethod]
    public void ABlockSmallerThanAPixelStillSamplesOnePixel() =>
        Assert.AreEqual('~', MapAsciiRenderer.Render(Tile(MapTestSupport.Png(MapTestSupport.Water)), 10.25, 10.5, 0.5, 1).Char);

    [Timeout(60_000)]
    [TestMethod]
    public void DecodingGarbageGivesNothing() =>
        Assert.IsNull(MapTile.Decode([1, 2, 3, 4, 5]));
}
