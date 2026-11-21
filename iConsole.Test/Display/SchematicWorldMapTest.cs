using iConsole.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Display;

[TestClass]
public class SchematicWorldMapTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void IsLand_IsTrueInsideAContinentAndFalseInTheOpenPacific()
    {
        // Budapest, on the Hungarian plain.
        Assert.IsTrue(SchematicWorldMap.IsLand(19, 47));
        // The middle of the Pacific, far from every outline.
        Assert.IsFalse(SchematicWorldMap.IsLand(-150, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LandCells_MarksTheCellOfBudapestAsLand()
    {
        // 72 columns of 5 degrees and 12 rows of 10 degrees: the cell centred on (17.5°E, 50°N) covers Budapest.
        bool[,] land = SchematicWorldMap.LandCells(72, 12);

        int column = SchematicWorldMap.ColumnOf(19, 72);
        int row = SchematicWorldMap.RowOf(47, 12);
        Assert.IsTrue(land[row, column], "row " + row + ", column " + column);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LandCells_LeavesTheOpenPacificAsSea()
    {
        bool[,] land = SchematicWorldMap.LandCells(72, 12);

        Assert.IsFalse(land[SchematicWorldMap.RowOf(0, 12), SchematicWorldMap.ColumnOf(-150, 72)]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_GivesTheRequestedNumberOfRowsOfTheRequestedWidth()
    {
        string[] rows = SchematicWorldMap.Render(72, 12, 82.5, 45);

        Assert.AreEqual(12, rows.Length);
        Assert.IsTrue(rows.All(row => row.Length == 72));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_PutsTheMarkerInTheRowOfItsLatitudeAndTheColumnOfItsLongitude()
    {
        // +05:30 is 82.5 degrees east: column 52 of 72. 45 degrees north is row 3 of 12.
        string[] rows = SchematicWorldMap.Render(72, 12, 82.5, 45);

        Assert.AreEqual('@', rows[3][52]);
        for (int row = 0; row < rows.Length; row++)
        {
            if (row != 3)
            {
                Assert.AreEqual('|', rows[row][52], "row " + row);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_DrawsLandAsHashAndSeaAsSpace()
    {
        string[] rows = SchematicWorldMap.Render(72, 12, 82.5, 45);
        bool[,] land = SchematicWorldMap.LandCells(72, 12);

        // Away from the meridian and the marker, each cell is the one the outlines give.
        for (int row = 0; row < 12; row++)
        {
            for (int column = 0; column < 72; column++)
            {
                if (column == 52)
                {
                    continue;
                }

                Assert.AreEqual(land[row, column] ? '#' : ' ', rows[row][column], "row " + row + ", column " + column);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RenderBraille_ReturnsTheRequestedNumberOfCharactersPerRow()
    {
        string[] rows = SchematicWorldMap.RenderBraille(21, 8, 15, 45);

        Assert.AreEqual(8, rows.Length);
        foreach (string row in rows)
        {
            Assert.AreEqual(21, row.Length);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RenderBraille_MarksTheMarkerCellOnTheMeridianInItsRow()
    {
        // 15°E is the meridian of UTC+1; with 21 characters it falls in dot column 22 of 42, which is character column 11.
        string[] rows = SchematicWorldMap.RenderBraille(21, 8, 15, 45);

        Assert.AreEqual('●', rows[SchematicWorldMap.RowOf(45, 32) / 4][SchematicWorldMap.ColumnOf(15, 42) / 2]);
        Assert.AreEqual(1, rows.Sum(row => row.Count(c => c == '●')));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RenderBraille_DrawsLandAsBraillePatterns()
    {
        // The bottom-left cell is open Pacific, so it is blank; some cell of the map is land.
        string[] rows = SchematicWorldMap.RenderBraille(21, 8, 15, 45);

        Assert.AreEqual('\u2800', rows[7][0]);
        Assert.IsTrue(rows.Any(row => row.Any(c => c is > '\u2800' and <= '\u28FF')));
    }
}
