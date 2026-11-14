using iConsole.Markdown;

namespace iConsole.Test.Markdown;

[TestClass]
public class MarkdownFileTypesTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("notes.md", true)]
    [DataRow("notes.MARKDOWN", true)]
    [DataRow("notes.txt", false)]
    [DataRow("notes", false)]
    public void IsMarkdownFile_RecognisesTheMarkdownEndings(string path, bool expected)
    {
        Assert.AreEqual(expected, MarkdownFileTypes.IsMarkdownFile(path));
    }
}
