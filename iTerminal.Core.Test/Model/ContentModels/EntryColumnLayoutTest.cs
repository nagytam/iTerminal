using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Test.Model.ContentModels;

[TestClass]
public class EntryColumnLayoutTest
{
    [TestMethod]
    public void AWidePanelShowsEveryColumn()
    {
        var (nameLength, showDate, showTime) = EntryBase.ColumnLayout(40);

        Assert.IsTrue(showDate);
        Assert.IsTrue(showTime);
        Assert.AreEqual(14, nameLength);
    }

    [TestMethod]
    [DataRow(30)]
    [DataRow(33)]
    [DataRow(20)]
    public void ANarrowPanelDropsTimeAndDateBeforeItSqueezesTheName(int width)
    {
        var (nameLength, showDate, showTime) = EntryBase.ColumnLayout(width);

        Assert.IsFalse(showTime);
        Assert.IsTrue(nameLength >= 11, $"name column of {nameLength} at width {width}");
        Assert.IsTrue(showDate || nameLength >= 14 || width < 31);
    }

    [TestMethod]
    [DataRow(60)]
    [DataRow(40)]
    [DataRow(36)]
    [DataRow(30)]
    [DataRow(24)]
    public void TheRowAlwaysFillsExactlyTheWidthItWasGiven(int width)
    {
        var (nameLength, showDate, showTime) = EntryBase.ColumnLayout(width);

        var used = nameLength + 1 + EntryBase.SizeLength
            + (showDate ? 1 + EntryBase.DateLength : 0) + (showTime ? 1 + EntryBase.TimeLength : 0);
        Assert.AreEqual(width - 2, used);
    }
}
