using iConsole.Controls;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Markdown;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Helpers that ran out of memory or threw for a null text or an absurd width (found by fuzzing the public static helpers).</summary>
[TestClass]
public class ControlsReviewBatch30Test
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void Table_NullEmptyText_IsEmpty_AndDraws()
    {
        Table<string> table = new([], _theme) { Position = new Position(0, 0), ViewSize = new Size(20, 3), EmptyText = null! };

        Assert.AreEqual(string.Empty, table.EmptyText);
        ScreenBuffer.Render(table, 20, 3);
    }

    [TestMethod]
    public void Expand_ToAnAbsurdWidth_IsCappedInsteadOfRunningOutOfMemory()
    {
        string padded = "ab".Expand(" ", int.MaxValue);

        Assert.IsTrue(padded.Length <= 100_002);
        Assert.AreEqual("ab", "ab".Expand(" ", int.MinValue));
    }

    [TestMethod]
    public void ExpandRight_ToAnAbsurdWidth_DoesNotOverflow()
    {
        Assert.AreEqual("ab", "ab".ExpandRight(int.MinValue));
        Assert.IsTrue("ab".ExpandRight(int.MaxValue).Length <= 100_000);
    }

    [TestMethod]
    public void Repeat_OfAnAbsurdCount_IsCapped()
    {
        Assert.IsTrue("a".Repeat(int.MaxValue).Length <= 100_002);
        Assert.IsTrue(StringHelper.Repeat('a', int.MaxValue).Length <= 100_000);
        Assert.AreEqual(string.Empty, string.Empty.Repeat(5));
    }

    [TestMethod]
    public void Cut_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, ((string?)null).Cut(5));
    }

    [TestMethod]
    public void LineEndings_OfNull_AreTheSystemsAndOneEmptyLine()
    {
        Assert.AreEqual(Environment.NewLine, LineEndings.Detect(null));
        CollectionAssert.AreEqual(new[] { string.Empty }, LineEndings.Split(null));
    }

    [TestMethod]
    public void OfficeTextRenderer_ColumnIndex_OfNull_IsZero()
    {
        Assert.AreEqual(0, OfficeTextRenderer.ColumnIndex(null));
    }

    [TestMethod]
    public void MarkdownInline_Parse_OfNull_IsNoSpans()
    {
        Assert.AreEqual(0, MarkdownInline.Parse(null).Count);
    }
}
