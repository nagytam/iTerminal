using iTerminal.DevelopmentStudio.Services.Analysis;

namespace iTerminal.DevelopmentStudio.Test.Services.Analysis;

[TestClass]
public class PythonProfileParserTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheSummaryLinesAndSkipsAnythingElse()
    {
        var entries = PythonProfileParser.Parse([
            "12\t0.001000\t0.050000\t/src/prog.py:2(add)",
            "1\t0.000100\t0.060000\t/src/prog.py:5(<module>)",
            "a traceback line",
        ]);

        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(12, entries[0].Calls);
        Assert.AreEqual(0.05, entries[0].CumulativeSeconds, 0.000001);
        Assert.AreEqual("/src/prog.py:5(<module>)", entries[1].Function);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSummaryScriptNamesTheTopFunctionsByCumulativeTime()
    {
        StringAssert.Contains(PythonProfileParser.SummaryScript, "sorted(stats.stats.items()");
        StringAssert.Contains(PythonProfileParser.SummaryScript, $"[:{PythonProfileParser.TopCount}]");
    }
}
