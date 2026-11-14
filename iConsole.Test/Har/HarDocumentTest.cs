using System.Text.Json;
using iConsole.Har;

namespace iConsole.Test.Har;

[TestClass]
public class HarDocumentTest
{
    private const string TwoEntries = """
        {
          "log": {
            "version": "1.2",
            "creator": { "name": "test", "version": "1" },
            "entries": [
              {
                "startedDateTime": "2026-11-14T09:30:00.000+00:00",
                "time": 12.5,
                "request": { "method": "GET", "url": "https://example.com/a" },
                "response": {
                  "status": 200, "statusText": "OK",
                  "content": { "size": 42, "mimeType": "text/html; charset=utf-8" }
                }
              },
              {
                "startedDateTime": "2026-11-14T09:30:01.000+00:00",
                "time": 80,
                "request": { "method": "POST", "url": "https://example.com/b" },
                "response": {
                  "status": 404, "statusText": "Not Found", "bodySize": 9,
                  "content": { "mimeType": "" }
                }
              }
            ]
          }
        }
        """;

    [TestMethod]
    [Timeout(60_000)]
    public void Parse_ReadsEveryEntryInOrderWithItsFields()
    {
        HarDocument document = HarDocument.Parse(TwoEntries);

        Assert.AreEqual(2, document.Entries.Count);
        Assert.IsFalse(document.Truncated);
        HarEntry first = document.Entries[0];
        Assert.AreEqual(1, first.Number);
        Assert.AreEqual("GET", first.Method);
        Assert.AreEqual("https://example.com/a", first.Url);
        Assert.AreEqual(200, first.Status);
        Assert.AreEqual("OK", first.StatusText);
        Assert.AreEqual("text/html; charset=utf-8", first.MimeType);
        Assert.AreEqual(42, first.Size);
        Assert.AreEqual(12.5, first.TimeMs);
        Assert.AreEqual("2026-11-14T09:30:00.000+00:00", first.StartedDateTime);
        Assert.AreEqual(2, document.Entries[1].Number);
        Assert.AreEqual("POST", document.Entries[1].Method);
        Assert.AreEqual(404, document.Entries[1].Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Parse_SizeFallsBackToTheBodySizeAndAnUnknownSizeIsNull()
    {
        const string json = """
            {"log":{"entries":[
              {"request":{},"response":{"bodySize":9,"content":{}}},
              {"request":{},"response":{"bodySize":-1,"content":{"size":-1}}},
              {"request":{},"response":{}}
            ]}}
            """;

        HarDocument document = HarDocument.Parse(json);

        Assert.AreEqual(9, document.Entries[0].Size, "the body's own size is missing, so the transfer size is used");
        Assert.IsNull(document.Entries[1].Size, "HAR's -1 means unknown");
        Assert.IsNull(document.Entries[2].Size);
        Assert.AreEqual(0, document.Entries[2].Status, "a response with no status has no status code");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Parse_SkipsEntriesThatAreNotObjects()
    {
        HarDocument document = HarDocument.Parse("""{"log":{"entries":[1,"x",{"request":{"method":"GET"}}]}}""");

        Assert.AreEqual(1, document.Entries.Count);
        Assert.AreEqual("GET", document.Entries[0].Method);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Parse_AJsonFileWithoutALogEntriesListIsNotHar()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => HarDocument.Parse("""{"name":"not a log"}"""));
        Assert.ThrowsExactly<InvalidDataException>(() => HarDocument.Parse("""{"log":{"entries":{}}}"""));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Parse_TextThatIsNotJsonThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => HarDocument.Parse("GET / HTTP/1.1"));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Parse_AnEmptyLogHasNoEntries()
    {
        HarDocument document = HarDocument.Parse("""{"log":{"entries":[]}}""");

        Assert.AreEqual(0, document.Entries.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow("example.har")]
    [DataRow("EXAMPLE.HAR")]
    public void IsHarFile_ChecksTheEnding(string name)
    {
        Assert.IsTrue(HarFileTypes.IsHarFile(name));
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow("example.json")]
    [DataRow("example.har.txt")]
    [DataRow("")]
    public void IsHarFile_RejectsOtherEndings(string name)
    {
        Assert.IsFalse(HarFileTypes.IsHarFile(name));
    }
}
