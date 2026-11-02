using System.Text;
using iTerminal.Core.Services.Search;

namespace iTerminal.Core.Test.Services.Search;

/// <summary>The content search's line reader: line numbers, line endings and matches that fall across its read chunks.</summary>
[TestClass]
public class FileSearchContentTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "filesearchcontent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string text)
    {
        string path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMatchFarPastTheFirstReadIsFoundOnItsLine()
    {
        var text = new StringBuilder();
        for (int i = 1; i < 3000; i++)
        {
            text.Append("filler line ").Append(i).Append(" with some padding text\n");
        }

        text.Append("the NEEDLE is here\n");
        text.Append("more after\n");

        var match = FileSearch.FindInFile(Write(text.ToString()), "needle", CancellationToken.None);

        Assert.IsNotNull(match);
        Assert.AreEqual(3000, match.Value.LineNumber);
        Assert.AreEqual("the NEEDLE is here", match.Value.LineText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CrLfAndLoneCarriageReturnsCountAsOneLineEachAcrossReads()
    {
        var text = new StringBuilder();
        for (int i = 1; i < 2000; i++)
        {
            text.Append("row ").Append(i).Append(i % 2 == 0 ? "\r\n" : "\r");
        }

        text.Append("target row\r\n");

        var match = FileSearch.FindInFile(Write(text.ToString()), "target", CancellationToken.None);

        Assert.IsNotNull(match);
        Assert.AreEqual(2000, match.Value.LineNumber);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMatchOnALineLongerThanOneReadIsFound()
    {
        string longLine = new string('x', 200_000) + "needle" + new string('y', 10);

        var match = FileSearch.FindInFile(Write("first\n" + longLine + "\nlast\n"), "needle", CancellationToken.None);

        Assert.IsNotNull(match);
        Assert.AreEqual(2, match.Value.LineNumber);
        Assert.IsTrue(match.Value.LineText.Contains("needle", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATermSpanningTwoLinesIsNotAMatch()
    {
        var match = FileSearch.FindInFile(Write("alpha\nbeta\n"), "a\nb", CancellationToken.None);

        Assert.IsNull(match);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLastLineWithoutALineEndIsSearched()
    {
        var match = FileSearch.FindInFile(Write("one\ntwo\nfind me"), "FIND", CancellationToken.None);

        Assert.IsNotNull(match);
        Assert.AreEqual(3, match.Value.LineNumber);
    }
}
