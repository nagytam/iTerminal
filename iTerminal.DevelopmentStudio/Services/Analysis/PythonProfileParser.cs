using System.Globalization;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services.Analysis;

/// <summary>
/// The profile of a Python run, from cProfile's stats file. <see cref="SummaryScript"/> prints it as tab-separated lines,
/// one function per line, which <see cref="Parse"/> reads back; the script runs under the same Python as the profiled program.
/// </summary>
public static class PythonProfileParser
{
    /// <summary>The number of functions the summary lists, by cumulative time.</summary>
    public const int TopCount = 40;

    /// <summary>A program that reads a cProfile stats file (argument 1) and prints the top functions by cumulative time.</summary>
    public static string SummaryScript =>
        "import pstats, sys\n" +
        "stats = pstats.Stats(sys.argv[1])\n" +
        "rows = sorted(stats.stats.items(), key=lambda item: item[1][3], reverse=True)[:" + TopCount + "]\n" +
        "for (file, line, name), (cc, nc, tt, ct, callers) in rows:\n" +
        "    print(f'{nc}\\t{tt:.6f}\\t{ct:.6f}\\t{file}:{line}({name})')\n";

    /// <summary>Parses the summary lines. Lines that are not four tab-separated fields are skipped.</summary>
    public static IReadOnlyList<ProfileEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<ProfileEntry>();
        foreach (string line in lines)
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 4) continue;
            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long calls)) continue;
            if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double total)) continue;
            if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cumulative)) continue;
            entries.Add(new ProfileEntry(calls, total, cumulative, fields[3]));
        }

        return entries;
    }
}
