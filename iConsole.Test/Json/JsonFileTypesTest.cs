using iConsole.Json;

namespace iConsole.Test.Json;

[TestClass]
public class JsonFileTypesTest
{
    [TestMethod]
    [DataRow("data.json")]
    [DataRow("Analysis.ipynb")]
    [DataRow("regions.GEOJSON")]
    public void JsonDocumentsOpenInTheJsonViewer(string name)
    {
        Assert.IsTrue(JsonFileTypes.IsJsonFile(name));
    }

    [TestMethod]
    [DataRow("notes.txt")]
    [DataRow("feed.jsonl")]
    public void OtherFilesDoNotOpenInTheJsonViewer(string name)
    {
        // JSON Lines holds one document per line, so the single-document viewer would reject it.
        Assert.IsFalse(JsonFileTypes.IsJsonFile(name));
    }
}
