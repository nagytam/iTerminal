using iConsole.Tabular;

namespace iConsole.Test.Tabular;

/// <summary>An N-dimensional array whose every cell says its own coordinates, so a test can see which axes a block really read.</summary>
internal sealed class CoordinateArray(long[] shape, string[]? fields = null) : IGridArraySource
{
    public List<(long[] Start, long[] Count)> Blocks { get; } = [];

    public IReadOnlyList<long> Shape { get; } = shape;

    public IReadOnlyList<string> ColumnNames { get; } = [];

    public IReadOnlyList<string> Fields { get; } = fields ?? [];

    public Task<IReadOnlyList<string>> ReadBlockAsync(long[] start, long[] count, CancellationToken cancellationToken)
    {
        Blocks.Add(([.. start], [.. count]));
        var cells = new List<string>();
        if (Fields.Count > 0)
        {
            // One-dimensional compound: an element per row, one cell per field.
            for (long element = start[0]; element < start[0] + count[0]; element++)
            {
                cells.AddRange(Fields.Select(field => $"{element}/{field}"));
            }

            return Task.FromResult<IReadOnlyList<string>>(cells);
        }

        if (count.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(["scalar"]);
        }

        foreach (long[] coordinate in Tuples(start, count, 0))
        {
            cells.Add(string.Join(".", coordinate));
        }

        return Task.FromResult<IReadOnlyList<string>>(cells);
    }

    /// <summary>Every coordinate in the block, the last axis fastest.</summary>
    private static IEnumerable<long[]> Tuples(long[] start, long[] count, int axis)
    {
        if (axis == start.Length)
        {
            yield return [];
            yield break;
        }

        for (long at = start[axis]; at < start[axis] + count[axis]; at++)
        {
            foreach (long[] rest in Tuples(start, count, axis + 1))
            {
                yield return [at, .. rest];
            }
        }
    }
}

[TestClass]
public class GridArrayViewTest
{
    [TestMethod]
    public void TheFirstViewShowsTheLastTwoAxesAndFixesTheRest()
    {
        var array = new CoordinateArray([4, 3, 2]);

        GridArrayView view = GridArrayView.Open(array);

        Assert.AreEqual(1, view.RowAxis);
        Assert.AreEqual(2, view.ColumnAxis);
        CollectionAssert.AreEqual(new[] { 0 }, view.FixedAxes.ToArray());
        Assert.AreEqual("[0, *, *]", view.Describe());
    }

    [TestMethod]
    public void IndexesAreClampedAndAShownAxisHasNoIndex()
    {
        GridArrayView view = GridArrayView.Open(new CoordinateArray([4, 3, 2]));

        Assert.AreEqual(3L, view.WithIndex(0, 99).IndexOf(0), "clamped to the axis");
        Assert.AreEqual(0L, view.Step(0, -5).IndexOf(0), "stepping below 0 stops at 0");
        Assert.Throws<ArgumentException>(() => view.WithIndex(1, 0));
    }

    [TestMethod]
    public async Task MaterializeReadsOnlyTheWindowAtTheFixedIndex()
    {
        var array = new CoordinateArray([4, 3, 2]);
        GridArrayView view = GridArrayView.Open(array).Step(0, 2);

        GridTable table = await view.MaterializeAsync(100, 100, 1000, CancellationToken.None);

        Assert.AreEqual("2.0.0", table.Rows[0][1], "row 0, column 0: the fixed index 2 on the first axis");
        Assert.AreEqual("2.2.1", table.Rows[2][2], "row 2, column 1");
        Assert.AreEqual(3, table.Rows.Count);
        CollectionAssert.AreEqual(new[] { "#", "0", "1" }, table.Headers.ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 0, 0 }, array.Blocks[^1].Start, "the fixed axis starts at its index");
        CollectionAssert.AreEqual(new long[] { 1, 3, 2 }, array.Blocks[^1].Count, "and spans one block along it");
    }

    [TestMethod]
    public async Task ProjectingOtherAxesReadsThemAsTheRowsAndColumns()
    {
        var array = new CoordinateArray([4, 3, 2]);
        GridArrayView view = GridArrayView.Open(array).Project(0, 2).Step(1, 1);

        GridTable table = await view.MaterializeAsync(100, 100, 1000, CancellationToken.None);

        Assert.AreEqual("[*, 1, *]", view.Describe());
        Assert.AreEqual("0.1.0", table.Rows[0][1], "axis 1 stays at its index 1; axis 0 is now the rows");
        Assert.AreEqual("3.1.1", table.Rows[3][2], "row 3 is axis 0 at 3; column 1 is axis 2 at 1");
        Assert.AreEqual(4, table.Rows.Count);
    }

    [TestMethod]
    public async Task TransposeSwapsTheRowsAndTheColumns()
    {
        GridArrayView view = GridArrayView.Open(new CoordinateArray([3, 2]));

        GridTable plain = await view.MaterializeAsync(100, 100, 1000, CancellationToken.None);
        GridTable turned = await view.Transpose().MaterializeAsync(100, 100, 1000, CancellationToken.None);

        Assert.AreEqual("0.1", plain.Rows[0][2], "row 0, column 1 is (0, 1)");
        Assert.AreEqual("1.0", turned.Rows[0][2], "transposed, row 0, column 1 is (1, 0)");
        Assert.AreEqual(2, turned.Rows.Count);
    }

    [TestMethod]
    public void AnAxisThatBecomesFixedKeepsItsIndex()
    {
        // Axis 1 is fixed only after the projection moves the rows to axis 0, so it can be stepped while it is fixed.
        GridArrayView fixedAt2 = GridArrayView.Open(new CoordinateArray([4, 3, 2])).Project(0, 2).Step(1, 2);

        GridArrayView shown = fixedAt2.Project(1, 2);
        GridArrayView fixedAgain = shown.Project(0, 2);

        Assert.AreEqual(2L, shown.IndexOf(1), "axis 1 keeps its index while it is shown as rows");
        Assert.AreEqual(2L, fixedAgain.IndexOf(1), "and when it becomes fixed again it is still at 2");
    }

    [TestMethod]
    public void ProjectionNeedsTwoDifferentAxesOfAnArrayWithTwoOrMore()
    {
        GridArrayView view = GridArrayView.Open(new CoordinateArray([4, 3, 2]));

        Assert.Throws<ArgumentException>(() => view.Project(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Project(0, 3));
        Assert.Throws<InvalidOperationException>(() => GridArrayView.Open(new CoordinateArray([5])).Project(0, 0));
    }

    [TestMethod]
    public async Task TheCapsCutTheRowsFirstAndSaySo()
    {
        GridArrayView view = GridArrayView.Open(new CoordinateArray([10, 20]));

        GridTable table = await view.MaterializeAsync(maxRows: 100, maxColumns: 5, maxCells: 12, CancellationToken.None);

        Assert.AreEqual(2, table.Rows.Count, "12 cells over 5 columns keep two rows");
        Assert.AreEqual(5, table.Headers.Count - 1);
        Assert.IsTrue(table.RowsTruncated);
        Assert.IsTrue(table.ColumnsTruncated);
    }

    [TestMethod]
    public async Task AOneDimensionalCompoundArrayHasOneColumnPerField()
    {
        var array = new CoordinateArray([2], ["id", "name"]);

        GridTable table = await GridArrayView.Open(array).MaterializeAsync(100, 100, 1000, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "#", "id", "name" }, table.Headers.ToArray());
        Assert.AreEqual("1/name", table.Rows[1][2]);
    }

    [TestMethod]
    public async Task AScalarIsOneCellAndAnEmptyAxisIsNoRows()
    {
        GridTable scalar = await GridArrayView.Open(new CoordinateArray([])).MaterializeAsync(100, 100, 1000, CancellationToken.None);
        GridTable empty = await GridArrayView.Open(new CoordinateArray([0, 3])).MaterializeAsync(100, 100, 1000, CancellationToken.None);

        Assert.AreEqual("scalar", scalar.Rows[0][0]);
        Assert.AreEqual(0, empty.Rows.Count);
    }

    [TestMethod]
    public async Task AnInMemoryTableIsATwoAxisArray()
    {
        string[][] rows = [["Anna", "5"], ["Bela", "20"]];
        var array = new InMemoryGridArray(rows, ["name", "amount"]);

        GridTable table = await GridArrayView.Open(array).MaterializeAsync(100, 100, 1000, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "#", "name", "amount" }, table.Headers.ToArray(), "a table's own column names, after the row number");
        Assert.AreEqual("Bela", table.Rows[1][1]);
        Assert.AreEqual("20", table.Rows[1][2]);
        GridTable plain = await GridArrayView.Open(array).MaterializeAsync(100, 100, 1000, CancellationToken.None, withRowNumbers: false);
        CollectionAssert.AreEqual(new[] { "name", "amount" }, plain.Headers.ToArray());
        Assert.AreEqual("Anna", plain.Rows[0][0]);
    }

    [TestMethod]
    public async Task TheLongFormQueryNamesEveryAxisAndTheValue()
    {
        GridArrayView view = GridArrayView.Open(new CoordinateArray([2, 3]));

        GridQueryResult result = await view.QueryLongFormAsync("SELECT axis0, axis1, value WHERE axis0 = 1 ORDER BY value DESC", 100, 1000, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "axis0", "axis1", "value" }, result.Headers.ToArray());
        Assert.AreEqual(3, result.Matched);
        CollectionAssert.AreEqual(new[] { "1", "2", "1.2" }, result.Rows[0]);
        Assert.IsFalse(result.Truncated);
    }

    [TestMethod]
    public async Task TheWholeArrayIsSearchedWhateverTheBlockSize()
    {
        var array = new CoordinateArray([4, 3, 2]);
        const string query = "WHERE axis2 = 1 ORDER BY value LIMIT 5";

        GridQueryResult oneCellAtATime = await GridArrayView.Open(array).QueryLongFormAsync(query, 1, 1000, CancellationToken.None);
        GridQueryResult aFewCells = await GridArrayView.Open(array).QueryLongFormAsync(query, 5, 1000, CancellationToken.None);
        GridQueryResult wholeArray = await GridArrayView.Open(array).QueryLongFormAsync(query, 100_000, 1000, CancellationToken.None);

        CollectionAssert.AreEqual(wholeArray.Rows.Select(r => string.Join('|', r)).ToArray(), oneCellAtATime.Rows.Select(r => string.Join('|', r)).ToArray());
        CollectionAssert.AreEqual(wholeArray.Rows.Select(r => string.Join('|', r)).ToArray(), aFewCells.Rows.Select(r => string.Join('|', r)).ToArray());
        Assert.AreEqual(12, wholeArray.Matched, "every cell with axis2 = 1 is a match: 4 x 3");
        Assert.AreEqual(5, wholeArray.Rows.Count, "the limit applies across the blocks");
    }

    [TestMethod]
    public async Task TheMatchCapSaysSoAndKeepsOnlyTheFirstMatches()
    {
        GridQueryResult result = await GridArrayView.Open(new CoordinateArray([4, 3, 2])).QueryLongFormAsync("WHERE axis0 >= 2", 5, 4, CancellationToken.None);

        Assert.AreEqual(4, result.Matched);
        Assert.AreEqual(4, result.Rows.Count);
        Assert.IsTrue(result.Truncated);
    }

    [TestMethod]
    public async Task TheRowsOfATableAreStreamedWhateverTheBlockSize()
    {
        string[][] rows = [["Anna", "5"], ["Bela", "20"], ["Cecil", ""], ["Dora", "100"]];
        var table = new InMemoryGridArray(rows, ["name", "amount"]);
        const string query = "SELECT name WHERE amount > 4 ORDER BY amount DESC";

        GridQueryResult oneRowAtATime = await GridArrayView.Open(table).QueryAsync(query, 2, int.MaxValue, CancellationToken.None);
        GridQueryResult wholeTable = await GridArrayView.Open(table).QueryAsync(query, 100_000, int.MaxValue, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "name" }, wholeTable.Headers.ToArray(), "the query's own columns");
        CollectionAssert.AreEqual(new[] { "Dora", "Bela", "Anna" }, wholeTable.Rows.Select(r => r[0]).ToArray());
        CollectionAssert.AreEqual(wholeTable.Rows.Select(r => r[0]).ToArray(), oneRowAtATime.Rows.Select(r => r[0]).ToArray(), "the same answer whatever the block size");
        Assert.AreEqual(3, wholeTable.Matched);
    }

    [TestMethod]
    public async Task ARowsQueryNamingAMissingColumnIsRefused()
    {
        var table = new InMemoryGridArray([["Anna", "5"]], ["name", "amount"]);

        await Assert.ThrowsExactlyAsync<GridQueryException>(() => GridArrayView.Open(table).QueryAsync("WHERE salary > 1", 100, int.MaxValue, CancellationToken.None));
    }
}
