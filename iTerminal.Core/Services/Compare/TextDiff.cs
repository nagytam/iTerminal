namespace iTerminal.Core.Services.Compare;

/// <summary>What a row of a side-by-side diff holds.</summary>
public enum DiffRowKind
{
    /// <summary>The same line on both sides.</summary>
    Same,

    /// <summary>A line on the left that the right does not have.</summary>
    LeftOnly,

    /// <summary>A line on the right that the left does not have.</summary>
    RightOnly,

    /// <summary>A line on each side, standing in the same place, that differ.</summary>
    Changed,
}

/// <summary>
/// One row of a side-by-side diff. Line numbers are 1-based, and null on the side with no line.
/// </summary>
public sealed record DiffRow(DiffRowKind Kind, int? LeftNumber, string? Left, int? RightNumber, string? Right);

/// <summary>Which differences between lines to disregard.</summary>
public sealed record TextDiffOptions(bool IgnoreWhitespace = false, bool IgnoreCase = false);

/// <summary>The outcome of comparing two texts line by line.</summary>
public sealed class TextDiffResult
{
    public TextDiffResult(IReadOnlyList<DiffRow> rows, bool isApproximate)
    {
        Rows = rows;
        IsApproximate = isApproximate;
        var hunks = new List<int>();
        var inside = false;
        for (var i = 0; i < rows.Count; i++)
        {
            var differs = rows[i].Kind != DiffRowKind.Same;
            if (differs && !inside)
            {
                hunks.Add(i);
            }

            inside = differs;
        }

        HunkStarts = hunks;
    }

    public IReadOnlyList<DiffRow> Rows { get; }

    /// <summary>The index of the first row of each run of differing rows.</summary>
    public IReadOnlyList<int> HunkStarts { get; }

    /// <summary>
    /// True when the files differ so much that the search for the shortest edit script was given up:
    /// the part between the lines they still share is shown as replaced rather than matched up.
    /// </summary>
    public bool IsApproximate { get; }

    public bool AreIdentical => HunkStarts.Count == 0;

    public int Changed => Rows.Count(r => r.Kind == DiffRowKind.Changed);

    public int Added => Rows.Count(r => r.Kind == DiffRowKind.RightOnly);

    public int Removed => Rows.Count(r => r.Kind == DiffRowKind.LeftOnly);

    public string Summary()
    {
        if (AreIdentical)
        {
            return "The files are identical.";
        }

        var text = $"{HunkStarts.Count} difference{(HunkStarts.Count == 1 ? string.Empty : "s")}: {Changed} changed, {Removed} only left, {Added} only right.";
        return IsApproximate ? text + " (very different — matched roughly)" : text;
    }
}

/// <summary>
/// A line diff (Myers' O(ND) shortest edit script) laid out for two columns. Common lines at both
/// ends are peeled off first, which is nearly the whole file in the usual case; a run of deletions
/// next to a run of insertions is paired into <see cref="DiffRowKind.Changed"/> rows, so an edited
/// line shows beside its old self rather than under it.
/// </summary>
public static class TextDiff
{
    /// <summary>
    /// How many edits the search goes through before it gives up and calls what is left between the
    /// shared ends one replaced block. Its memory is the square of this.
    /// </summary>
    internal const int MaxEdits = 3000;

    public static TextDiffResult Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, TextDiffOptions? options = null)
    {
        options ??= new TextDiffOptions();
        var leftKeys = left.Select(line => Normalize(line, options)).ToArray();
        var rightKeys = right.Select(line => Normalize(line, options)).ToArray();

        var prefix = 0;
        while (prefix < leftKeys.Length && prefix < rightKeys.Length && leftKeys[prefix] == rightKeys[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < leftKeys.Length - prefix && suffix < rightKeys.Length - prefix
            && leftKeys[leftKeys.Length - 1 - suffix] == rightKeys[rightKeys.Length - 1 - suffix])
        {
            suffix++;
        }

        var a = leftKeys.AsSpan(prefix, leftKeys.Length - prefix - suffix).ToArray();
        var b = rightKeys.AsSpan(prefix, rightKeys.Length - prefix - suffix).ToArray();
        var ops = Script(a, b, out var approximate);

        var rows = new List<DiffRow>(left.Count + right.Count);
        for (var i = 0; i < prefix; i++)
        {
            rows.Add(new DiffRow(DiffRowKind.Same, i + 1, left[i], i + 1, right[i]));
        }

        var x = prefix;
        var y = prefix;
        var deleted = new List<int>();
        var inserted = new List<int>();

        void Flush()
        {
            var pairs = Math.Min(deleted.Count, inserted.Count);
            for (var p = 0; p < pairs; p++)
            {
                rows.Add(new DiffRow(DiffRowKind.Changed, deleted[p] + 1, left[deleted[p]], inserted[p] + 1, right[inserted[p]]));
            }

            for (var p = pairs; p < deleted.Count; p++)
            {
                rows.Add(new DiffRow(DiffRowKind.LeftOnly, deleted[p] + 1, left[deleted[p]], null, null));
            }

            for (var p = pairs; p < inserted.Count; p++)
            {
                rows.Add(new DiffRow(DiffRowKind.RightOnly, null, null, inserted[p] + 1, right[inserted[p]]));
            }

            deleted.Clear();
            inserted.Clear();
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case Op.Equal:
                    Flush();
                    rows.Add(new DiffRow(DiffRowKind.Same, x + 1, left[x], y + 1, right[y]));
                    x++;
                    y++;
                    break;
                case Op.Delete:
                    deleted.Add(x++);
                    break;
                default:
                    inserted.Add(y++);
                    break;
            }
        }

        Flush();

        for (var i = 0; i < suffix; i++)
        {
            var li = leftKeys.Length - suffix + i;
            var ri = rightKeys.Length - suffix + i;
            rows.Add(new DiffRow(DiffRowKind.Same, li + 1, left[li], ri + 1, right[ri]));
        }

        return new TextDiffResult(rows, approximate);
    }

    private enum Op
    {
        Equal,
        Delete,
        Insert,
    }

    private static string Normalize(string line, TextDiffOptions options)
    {
        if (options.IgnoreWhitespace)
        {
            var chars = new List<char>(line.Length);
            var pendingBlank = false;
            foreach (var c in line)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingBlank = chars.Count > 0;
                    continue;
                }

                if (pendingBlank)
                {
                    chars.Add(' ');
                    pendingBlank = false;
                }

                chars.Add(c);
            }

            line = new string([.. chars]);
        }

        return options.IgnoreCase ? line.ToUpperInvariant() : line;
    }

    /// <summary>The edit script from <paramref name="a"/> to <paramref name="b"/>, in order.</summary>
    private static List<Op> Script(string[] a, string[] b, out bool approximate)
    {
        approximate = false;
        var n = a.Length;
        var m = b.Length;
        var ops = new List<Op>(n + m);

        if (n == 0 || m == 0)
        {
            ops.AddRange(Enumerable.Repeat(Op.Delete, n));
            ops.AddRange(Enumerable.Repeat(Op.Insert, m));
            return ops;
        }

        var cap = Math.Min(n + m, MaxEdits);
        var offset = cap + 1;
        var v = new int[2 * cap + 3];
        var trace = new List<int[]>();
        var found = -1;

        for (var d = 0; d <= cap && found < 0; d++)
        {
            // What the previous round left, for k in [-d-1, d+1] — backtracking reads it.
            var snapshot = new int[2 * d + 3];
            Array.Copy(v, offset - d - 1, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                {
                    x = v[offset + k + 1];
                }
                else
                {
                    x = v[offset + k - 1] + 1;
                }

                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    found = d;
                    break;
                }
            }
        }

        if (found < 0)
        {
            // Too different to search out: what is between the shared ends is shown as replaced.
            approximate = true;
            ops.AddRange(Enumerable.Repeat(Op.Delete, n));
            ops.AddRange(Enumerable.Repeat(Op.Insert, m));
            return ops;
        }

        var reversed = new List<Op>(n + m);
        var cx = n;
        var cy = m;
        for (var d = found; d > 0; d--)
        {
            var snapshot = trace[d];
            int At(int k) => snapshot[k + d + 1];
            var k = cx - cy;
            var previousK = k == -d || (k != d && At(k - 1) < At(k + 1)) ? k + 1 : k - 1;
            var previousX = At(previousK);
            var previousY = previousX - previousK;

            while (cx > previousX && cy > previousY)
            {
                reversed.Add(Op.Equal);
                cx--;
                cy--;
            }

            if (cx == previousX)
            {
                reversed.Add(Op.Insert);
                cy--;
            }
            else
            {
                reversed.Add(Op.Delete);
                cx--;
            }
        }

        while (cx > 0 && cy > 0)
        {
            reversed.Add(Op.Equal);
            cx--;
            cy--;
        }

        reversed.Reverse();
        return reversed;
    }
}
