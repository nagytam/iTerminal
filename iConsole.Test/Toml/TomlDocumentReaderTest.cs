using System.Text.Json.Nodes;
using iConsole.Test.Common;
using iConsole.Toml;
using Tomlyn;

namespace iConsole.Test.Toml;

[TestClass]
public class TomlDocumentReaderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Read_ReadsTablesArraysOfTablesAndScalars()
    {
        JsonObject root = TomlDocumentReader.Read("""
            title = "x"
            count = 3
            ratio = 0.5
            enabled = true
            tags = ["a", "b"]

            [owner]
            name = "T"

            [[products]]
            sku = 1

            [[products]]
            sku = 2
            """)!.AsObject();

        Assert.AreEqual("x", root["title"]!.GetValue<string>());
        Assert.AreEqual(3L, root["count"]!.GetValue<long>());
        Assert.AreEqual(0.5, root["ratio"]!.GetValue<double>());
        Assert.IsTrue(root["enabled"]!.GetValue<bool>());
        Assert.AreEqual(2, root["tags"]!.AsArray().Count);
        Assert.AreEqual("T", root["owner"]!["name"]!.GetValue<string>());
        Assert.AreEqual(2, root["products"]!.AsArray().Count);
        Assert.AreEqual(2L, root["products"]!.AsArray()[1]!["sku"]!.GetValue<long>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_DatesAndTimesStayAsTheirTomlText()
    {
        JsonObject root = TomlDocumentReader.Read("day = 2026-03-05\nat = 2026-03-05T10:20:30Z\n")!.AsObject();

        Assert.AreEqual("2026-03-05", root["day"]!.GetValue<string>());
        StringAssert.StartsWith(root["at"]!.GetValue<string>(), "2026-03-05T10:20:30");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_ASyntaxErrorThrows()
    {
        Assert.ThrowsExactly<TomlException>(() => TomlDocumentReader.Read("a = = 1"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_TheExampleFileReadsItsTablesAndArrays()
    {
        JsonObject root = TomlDocumentReader.Read(File.ReadAllText(SampleFiles.Path("example.toml")))!.AsObject();

        Assert.AreEqual("iTerminal example", root["package"]!["name"]!.GetValue<string>());
        Assert.AreEqual("1.0.0", root["package"]!["version"]!.GetValue<string>());
    }
}
