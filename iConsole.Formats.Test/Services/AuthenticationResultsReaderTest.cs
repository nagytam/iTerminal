using iConsole.Formats.Services;

namespace iConsole.Formats.Test.Services;

/// <summary>The Authentication-Results header (RFC 8601): the receiver's identifier and the checks it recorded.</summary>
[TestClass]
public class AuthenticationResultsReaderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheServerAndEachMethodAndResult()
    {
        AuthenticationResultsReader.Summary? summary = AuthenticationResultsReader.Parse(
            "mx.example.org 1; SPF=Pass smtp.mailfrom=a@example.org; dkim=fail reason=\"bad signature\" header.d=example.org; dmarc=none");

        Assert.IsNotNull(summary);
        Assert.AreEqual("mx.example.org", summary.Server);
        CollectionAssert.AreEqual(
            new[] { "spf=pass", "dkim=fail", "dmarc=none" },
            summary.Checks.Select(c => $"{c.Method}={c.Result}").ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoneMeansNoChecks()
    {
        AuthenticationResultsReader.Summary? summary = AuthenticationResultsReader.Parse("mx.example.org; none");

        Assert.AreEqual(0, summary!.Checks.Count);
        StringAssert.Contains(AuthenticationResultsReader.FormatLine("mx.example.org; none")!, "no checks recorded");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommentsAreIgnoredEvenWhenNested()
    {
        AuthenticationResultsReader.Summary? summary = AuthenticationResultsReader.Parse("mx.example.org (Postfix (v3)); spf=pass (sender is ok; really)");

        Assert.AreEqual("mx.example.org", summary!.Server);
        Assert.AreEqual("spf", summary.Checks.Single().Method);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void EmptyValueGivesNothing(string? value)
    {
        Assert.IsNull(AuthenticationResultsReader.Parse(value));
        Assert.IsNull(AuthenticationResultsReader.FormatLine(value));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MalformedEntriesAreSkippedAndControlCharactersDropped()
    {
        string? line = AuthenticationResultsReader.FormatLine("mx\u001b.example.org; garbage; =x; spf=; dkim=pass");

        Assert.AreEqual("Authentication-Results (as recorded by mx.example.org, not checked here; any sender can write this header): dkim=pass", line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AVeryLongValueIsCutAndStillParses()
    {
        string value = "mx.example.org; " + string.Concat(Enumerable.Repeat("x=y; ", 5000));

        AuthenticationResultsReader.Summary? summary = AuthenticationResultsReader.Parse(value);

        Assert.IsTrue(summary!.Checks.Count < 1000);
    }
}
