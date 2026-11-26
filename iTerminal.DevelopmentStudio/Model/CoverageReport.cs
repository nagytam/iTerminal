namespace iTerminal.DevelopmentStudio.Model;

/// <summary>Line coverage of one source file. <see cref="Hits"/> maps each instrumented line (1-based) to its hit count.</summary>
public sealed record CoverageFile(string Path, IReadOnlyDictionary<int, int> Hits)
{
    /// <summary>Lines the tool instrumented.</summary>
    public int ValidLines => Hits.Count;

    /// <summary>Instrumented lines that ran at least once.</summary>
    public int CoveredLines => Hits.Values.Count(hit => hit > 0);

    /// <summary>The covered share of the instrumented lines, 0 to 100 (100 for a file with no lines).</summary>
    public double Percent => ValidLines == 0 ? 100 : 100.0 * CoveredLines / ValidLines;
}

/// <summary>The coverage of a run: every file the tool reported, and the totals over them.</summary>
public sealed record CoverageReport(IReadOnlyList<CoverageFile> Files)
{
    public int ValidLines => Files.Sum(file => file.ValidLines);

    public int CoveredLines => Files.Sum(file => file.CoveredLines);

    public double Percent => ValidLines == 0 ? 100 : 100.0 * CoveredLines / ValidLines;
}
