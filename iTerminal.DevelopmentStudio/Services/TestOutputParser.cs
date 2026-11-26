using System.Text.RegularExpressions;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>Pass, fail and skip counts of one test run.</summary>
public sealed record TestTotals(int Passed, int Failed, int Skipped)
{
    public static readonly TestTotals Empty = new(0, 0, 0);

    public TestTotals Add(TestTotals other) => new(Passed + other.Passed, Failed + other.Failed, Skipped + other.Skipped);

    public int Total => Passed + Failed + Skipped;
}

/// <summary>
/// Reads the summary of a test run from its output: VSTest (<c>dotnet test</c>), pytest and Pester. Only summary lines are
/// matched, so a test's own printed text does not count.
/// </summary>
public static partial class TestOutputParser
{
    // VSTest: "Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 1 s - Foo.dll"
    [GeneratedRegex(@"^(?:Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)")]
    private static partial Regex VsTestSummary();

    // Pester 5: "Tests Passed: 3, Failed: 1, Skipped: 0, NotRun: 0"
    [GeneratedRegex(@"Tests Passed:\s*(\d+),\s*Failed:\s*(\d+),\s*Skipped:\s*(\d+)")]
    private static partial Regex PesterSummary();

    // pytest's closing line: "=== 2 passed, 1 failed, 1 skipped in 0.12s ===" (the "=== " is absent with -q)
    [GeneratedRegex(@"^=*\s*((?:\d+ \w+,?\s*)+)\s+in\s+[\d.]+s")]
    private static partial Regex PytestSummary();

    [GeneratedRegex(@"(\d+) (passed|failed|skipped|error|errors|xfailed|xpassed)")]
    private static partial Regex PytestCount();

    /// <summary>
    /// The totals of a run, from the summary lines of <paramref name="output"/>. A run can print several summaries (one per
    /// test project of a solution), so their counts are added. Returns <see cref="TestTotals.Empty"/> when there is none.
    /// </summary>
    public static TestTotals Parse(IEnumerable<string> output)
    {
        TestTotals totals = TestTotals.Empty;
        foreach (string line in output)
        {
            string trimmed = line.Trim();
            var vs = VsTestSummary().Match(trimmed);
            if (vs.Success)
            {
                totals = totals.Add(new TestTotals(int.Parse(vs.Groups[2].Value), int.Parse(vs.Groups[1].Value), int.Parse(vs.Groups[3].Value)));
                continue;
            }

            var pester = PesterSummary().Match(trimmed);
            if (pester.Success)
            {
                totals = totals.Add(new TestTotals(int.Parse(pester.Groups[1].Value), int.Parse(pester.Groups[2].Value), int.Parse(pester.Groups[3].Value)));
                continue;
            }

            var pytest = PytestSummary().Match(trimmed);
            if (pytest.Success)
            {
                int passed = 0, failed = 0, skipped = 0;
                foreach (GroupCollection count in PytestCount().Matches(pytest.Groups[1].Value).Select(m => m.Groups))
                {
                    int value = int.Parse(count[1].Value);
                    switch (count[2].Value)
                    {
                        case "passed": passed += value; break;
                        case "failed": case "error": case "errors": failed += value; break;
                        case "skipped": skipped += value; break;
                    }
                }

                totals = totals.Add(new TestTotals(passed, failed, skipped));
            }
        }

        return totals;
    }
}
