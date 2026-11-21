using System.Text.Json.Nodes;
using iConsole.Json;
using iConsole.Test.Common;
using iConsole.Yaml;
using YamlDotNet.Core;

namespace iConsole.Test.Yaml;

[TestClass]
public class YamlDocumentReaderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Read_ReadsMappingsSequencesAndTheCoreScalars()
    {
        JsonNode? root = YamlDocumentReader.Read("""
            name: iTerminal
            count: 3
            ratio: 0.5
            on: true
            nothing: ~
            quoted: "3"
            hex: 0x1F
            list:
              - a
              - 2
            """);

        var obj = root!.AsObject();
        Assert.AreEqual("iTerminal", obj["name"]!.GetValue<string>());
        Assert.AreEqual(3L, obj["count"]!.GetValue<long>());
        Assert.AreEqual(0.5, obj["ratio"]!.GetValue<double>());
        Assert.IsTrue(obj["on"]!.GetValue<bool>());
        Assert.IsNull(obj["nothing"]);
        Assert.AreEqual("3", obj["quoted"]!.GetValue<string>(), "a quoted number is text");
        Assert.AreEqual(31L, obj["hex"]!.GetValue<long>());
        Assert.AreEqual(2, obj["list"]!.AsArray().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_SeveralDocumentsReadAsAnArrayOfThem()
    {
        JsonArray documents = YamlDocumentReader.Read("a: 1\n---\nb: 2\n")!.AsArray();

        Assert.AreEqual(2, documents.Count);
        Assert.AreEqual(1L, documents[0]!["a"]!.GetValue<long>());
        Assert.AreEqual(2L, documents[1]!["b"]!.GetValue<long>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_EmptyTextIsNull()
    {
        Assert.IsNull(YamlDocumentReader.Read(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_ARepeatedAliasIsAllowed()
    {
        JsonObject root = YamlDocumentReader.Read("a: &x {k: 1}\nb: *x\n")!.AsObject();

        Assert.AreEqual(1L, root["b"]!["k"]!.GetValue<long>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_AnAliasThatPointsAtItsOwnParentIsRefused()
    {
        Assert.ThrowsExactly<FormatException>(() => YamlDocumentReader.Read("a: &x\n  b: *x\n"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_ASyntaxErrorThrows()
    {
        Assert.Throws<YamlException>(() => YamlDocumentReader.Read("a: [1, 2"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_NestingDeeperThanTheViewerReadsIsRefused()
    {
        string text = new string('[', 300) + new string(']', 300);

        Assert.ThrowsExactly<FormatException>(() => YamlDocumentReader.Read(text));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_TheExampleFileReadsItsScalarsAndSequence()
    {
        JsonObject root = YamlDocumentReader.Read(File.ReadAllText(SampleFiles.Path("example.yaml")))!.AsObject();

        Assert.AreEqual("iTerminal example", root["name"]!.GetValue<string>());
        Assert.AreEqual(1L, root["version"]!.GetValue<long>());
        Assert.AreEqual(2, root["tags"]!.AsArray().Count);
    }
}
