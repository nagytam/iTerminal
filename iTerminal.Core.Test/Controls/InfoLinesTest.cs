using iConsole.Data;
using iConsole.Display;
using iTerminal.Core.Controls;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.Test;

/// <summary>A block of read-only facts: each row in its own colour, the text readable, and nothing left behind by a shorter value.</summary>
[TestClass]
public class InfoLinesTest
{
    private static InfoLines NewLines(Size size)
    {
        var lines = new InfoLines(TestTheme()) { Position = new Position(0, 0), ViewSize = size };
        return lines;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Text_JoinsTheRowsOnSeparateLines()
    {
        var lines = NewLines(new Size(30, 3));

        lines.Lines = [new InfoLines.Line("Name: a"), new InfoLines.Line("Size: 4 B")];

        Assert.AreEqual("Name: a" + Environment.NewLine + "Size: 4 B", lines.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Lines_Null_IsNoRows()
    {
        var lines = NewLines(new Size(30, 3));
        lines.Lines = [new InfoLines.Line("x")];

        lines.Lines = null!;

        Assert.AreEqual(string.Empty, lines.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PaintsEachRow_AndTheRestOfTheBlockIsBlank()
    {
        var lines = NewLines(new Size(30, 3));
        lines.Lines = [new InfoLines.Line("Verdict: safe", Color.Green)];

        var screen = Render(lines, new Size(30, 3));

        AssertScreenContains(screen, "Verdict: safe");
    }
}
