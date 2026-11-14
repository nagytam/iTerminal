using iConsole.Har;

namespace iConsole.Test.Har;

[TestClass]
public class HarEntryDetailsTest
{
    /// <summary>The first entry of a one-entry log, with the given entry body inserted.</summary>
    private static HarEntry EntryFrom(string entryJson)
    {
        HarDocument document = HarDocument.Parse("{\"log\":{\"entries\":[" + entryJson + "]}}");
        return document.Entries[0];
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_ShowsTheRequestLineStatusQueryAndHeaders()
    {
        HarEntry entry = EntryFrom("""
            {
              "startedDateTime": "2026-11-14T09:30:00.000+00:00",
              "time": 33,
              "serverIPAddress": "93.184.216.34",
              "request": {
                "method": "GET",
                "url": "https://example.com/api/users?page=2",
                "queryString": [ { "name": "page", "value": "2" } ],
                "headers": [ { "name": "Accept", "value": "application/json" } ]
              },
              "response": {
                "status": 200, "statusText": "OK",
                "headers": [ { "name": "Content-Type", "value": "application/json" } ],
                "content": { "mimeType": "application/json", "text": "{}" }
              }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.AreEqual("GET https://example.com/api/users?page=2", lines[0]);
        Assert.IsTrue(lines[1].StartsWith("Status: 200 OK", StringComparison.Ordinal), lines[1]);
        Assert.IsTrue(lines.Contains("Server: 93.184.216.34"));
        Assert.IsTrue(lines.Contains("Query string"));
        Assert.IsTrue(lines.Contains("  page = 2"));
        Assert.IsTrue(lines.Contains("Request headers"));
        Assert.IsTrue(lines.Contains("  Accept: application/json"));
        Assert.IsTrue(lines.Contains("Response headers"));
        Assert.IsTrue(lines.Contains("  Content-Type: application/json"));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_PrettyPrintsAJsonBodyAndKeepsItsNonAsciiText()
    {
        HarEntry entry = EntryFrom("""
            {
              "request": { "method": "GET", "url": "https://example.com/u" },
              "response": {
                "status": 200,
                "content": { "mimeType": "application/json", "text": "{\"name\":\"Ádám\",\"page\":2}" }
              }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        // Indented once by the JSON writer and once more as a body line.
        Assert.IsTrue(lines.Contains("    \"page\": 2"), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(line => line.Contains("Ádám")), "the body is not escaped to \\u00C1");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_ShowsABase64TextBodyDecoded()
    {
        // "Hello, world" in base64.
        HarEntry entry = EntryFrom("""
            {
              "request": { "method": "GET", "url": "https://example.com/t" },
              "response": {
                "status": 200,
                "content": { "mimeType": "text/plain", "encoding": "base64", "text": "SGVsbG8sIHdvcmxk" }
              }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.IsTrue(lines.Contains("  Hello, world"), string.Join("\n", lines));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_NamesABinaryBodyBySizeAndDoesNotShowIt()
    {
        // A 1x1 PNG: 70 bytes, base64 encoded.
        HarEntry entry = EntryFrom("""
            {
              "request": { "method": "GET", "url": "https://example.com/logo.png" },
              "response": {
                "status": 200,
                "content": {
                  "mimeType": "image/png", "encoding": "base64",
                  "text": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="
                }
              }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.IsTrue(lines.Contains("  (binary, 70 bytes, not shown)"), string.Join("\n", lines));
        Assert.IsFalse(lines.Any(line => line.Contains("PNG")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_ShowsAFormPostAsItsParameters()
    {
        HarEntry entry = EntryFrom("""
            {
              "request": {
                "method": "POST", "url": "https://example.com/form",
                "postData": {
                  "mimeType": "application/x-www-form-urlencoded",
                  "params": [ { "name": "user", "value": "demo" } ]
                }
              },
              "response": { "status": 302 }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.IsTrue(lines.Contains("Request body (application/x-www-form-urlencoded)"), string.Join("\n", lines));
        Assert.IsTrue(lines.Contains("  user = demo"));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_ShowsAnEmptyBodyAsNoBody()
    {
        HarEntry entry = EntryFrom("""{"request":{"method":"GET","url":"https://example.com/"},"response":{"status":204}}""");

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.IsTrue(lines.Contains("  (no body)"), string.Join("\n", lines));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_ListsOnlyTheTimingPhasesThatHappened()
    {
        HarEntry entry = EntryFrom("""
            {
              "request": { "method": "GET", "url": "https://example.com/" },
              "response": { "status": 200 },
              "timings": { "blocked": 0.2, "dns": -1, "connect": -1, "send": 0.1, "wait": 12.9, "receive": 1.4, "ssl": -1 }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.IsTrue(lines.Any(line => line.StartsWith("  wait ", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.IsFalse(lines.Any(line => line.Contains("dns")), "HAR's -1 means the phase did not happen");
        Assert.IsFalse(lines.Any(line => line.Contains("ssl")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Build_CutsAVeryLongBodyAndSaysSo()
    {
        string text = new string('a', HarEntryDetails.MaxBodyCharacters + 10);
        HarEntry entry = EntryFrom($$"""
            {
              "request": { "method": "GET", "url": "https://example.com/big" },
              "response": { "status": 200, "content": { "mimeType": "text/plain", "text": "{{text}}" } }
            }
            """);

        IReadOnlyList<string> lines = HarEntryDetails.Build(entry);

        Assert.IsTrue(lines.Any(line => line.StartsWith("  (cut at ", StringComparison.Ordinal)), "the cut is named");
        Assert.IsTrue(lines.Max(line => line.Length) <= HarEntryDetails.MaxBodyCharacters + 2 + 40);
    }
}
