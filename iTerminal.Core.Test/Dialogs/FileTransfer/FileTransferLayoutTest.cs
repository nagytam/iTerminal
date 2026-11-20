using iTerminal.Core.Dialogs.FileTransfer;

namespace iTerminal.Core.Test.Dialogs.FileTransfer;

[TestClass]
public class FileTransferLayoutTest
{
    [TestMethod]
    public void Plan_TallDialog_ShowsTheFullLayoutWithTheStandardQuietZone()
    {
        // Version 4 is 33 modules: 21 rows with a quiet zone of four.
        FileTransferLayoutPlan plan = FileTransferLayout.Plan(innerHeight: 28, urlRows: 1, codeModules: 33, warningRows: 3);

        Assert.IsTrue(plan.ShowsCode);
        Assert.AreEqual(4, plan.QuietZone);
        Assert.AreEqual(0, plan.SelectionRow, "the selection line stays in the full layout");
        Assert.AreEqual(3, plan.CodeTop);
        Assert.IsTrue(plan.CodeTop + FileTransferLayout.CodeRows(33, 4) <= plan.WarningTop, "the code does not reach the warning");
    }

    [TestMethod]
    public void Plan_On24RowTerminal_KeepsTheCodeInACompactForm()
    {
        // Inner height 22 is an 80x24 terminal with the dialog filling it.
        FileTransferLayoutPlan plan = FileTransferLayout.Plan(innerHeight: 22, urlRows: 1, codeModules: 33, warningRows: 2);

        Assert.IsTrue(plan.ShowsCode, "a standard terminal still shows the code");
        Assert.AreEqual(2, plan.QuietZone, "the quiet zone is reduced to fit");
        Assert.AreEqual(-1, plan.SelectionRow, "the selection line is given up first");
        Assert.IsTrue(plan.CodeTop + FileTransferLayout.CodeRows(33, 2) <= plan.WarningTop);
        Assert.IsTrue(plan.StatusRow < 22, "the status line stays on screen");
    }

    [TestMethod]
    public void Plan_WhenNothingFits_DropsTheCodeAndKeepsTheUrl()
    {
        FileTransferLayoutPlan plan = FileTransferLayout.Plan(innerHeight: 6, urlRows: 2, codeModules: 33, warningRows: 3);

        Assert.IsFalse(plan.ShowsCode);
        Assert.AreEqual(1, plan.UrlTop, "the URL keeps its rows");
        Assert.IsTrue(plan.WarningRows <= 6 - 1 - 2, "the warning gives way to the URL");
    }

    [TestMethod]
    public void Plan_WithoutAnyCode_LaysOutTheUrlAndWarningOnly()
    {
        FileTransferLayoutPlan plan = FileTransferLayout.Plan(innerHeight: 10, urlRows: 1, codeModules: 0, warningRows: 2);

        Assert.IsFalse(plan.ShowsCode);
        Assert.AreEqual(2, plan.WarningRows);
        Assert.AreEqual(9, plan.StatusRow);
    }

    [TestMethod]
    public void CodeRows_RoundsUpToWholeTextLines()
    {
        Assert.AreEqual(21, FileTransferLayout.CodeRows(33, 4));
        Assert.AreEqual(19, FileTransferLayout.CodeRows(33, 2));
    }

    [TestMethod]
    public void WrapText_Url_IsCutIntoLinesOfTheWidth()
    {
        IReadOnlyList<string> lines = FileTransferLayout.WrapText("https://192.168.1.20:8080/abcdef/", 10, 3, breakAnywhere: true);

        CollectionAssert.AreEqual(new[] { "https://19", "2.168.1.20", ":8080/abcd" }, lines.ToArray(), "the first three lines; the rest is cut");
    }

    [TestMethod]
    public void WrapText_Text_IsNeverWiderThanTheWidth()
    {
        IReadOnlyList<string> lines = FileTransferLayout.WrapText("one two three four five six seven eight", 9, 99, breakAnywhere: false);

        Assert.IsTrue(lines.All(line => line.Length <= 9));
    }
}
