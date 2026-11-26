using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class TestOutputParserTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheVsTestSummaryOfDotnetTest()
    {
        var totals = TestOutputParser.Parse(["Passed!  - Failed: 1, Passed: 4, Skipped: 2, Total: 7, Duration: 1 s - App.Test.dll"]);

        Assert.AreEqual(new TestTotals(4, 1, 2), totals);
        Assert.AreEqual(7, totals.Total);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddsTheSummariesOfSeveralTestProjects()
    {
        var totals = TestOutputParser.Parse([
            "Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 1 s - A.dll",
            "Failed!  - Failed: 3, Passed: 1, Skipped: 0, Total: 4, Duration: 1 s - B.dll",
        ]);

        Assert.AreEqual(new TestTotals(3, 3, 0), totals);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsThePytestClosingLine()
    {
        var totals = TestOutputParser.Parse([".F.s", "=== 2 passed, 1 failed, 1 skipped in 0.12s ==="]);

        Assert.AreEqual(new TestTotals(2, 1, 1), totals);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsPesterTotals()
    {
        var totals = TestOutputParser.Parse(["Tests Passed: 3, Failed: 1, Skipped: 0, NotRun: 0"]);

        Assert.AreEqual(new TestTotals(3, 1, 0), totals);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IgnoresLinesThatOnlyMentionPassing()
    {
        var totals = TestOutputParser.Parse(["Test Passed: the login works", "3 passed in the cache, nothing more"]);

        Assert.AreEqual(TestTotals.Empty, totals);
    }
}
