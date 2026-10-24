namespace iTerminal.Core.Services.Compare;

/// <summary>A run of bytes that differ between two files, or the bytes one file has past the other's end.</summary>
public sealed record ByteDifference(long Start, long Length);

/// <summary>The byte-by-byte comparison of two files: where they differ, and how long each is.</summary>
/// <param name="Runs">The differing runs, in order, none of them touching the next.</param>
public sealed record BinaryDiffResult(long LeftLength, long RightLength, IReadOnlyList<ByteDifference> Runs)
{
    public bool Identical => Runs.Count == 0;

    public long DifferingBytes => Runs.Sum(run => run.Length);

    /// <summary>True when the byte at <paramref name="offset"/> differs, or is past the end of one of the files.</summary>
    public bool IsDifferent(long offset)
    {
        // The last run that starts at or before the offset is the only one that can cover it.
        var low = 0;
        var high = Runs.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (Runs[middle].Start <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 && offset < Runs[found].Start + Runs[found].Length;
    }
}

/// <summary>Compares two files byte by byte, for files that are not text or for a byte view of a text file.</summary>
public static class BinaryComparer
{
    public static BinaryDiffResult Compare(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var runs = new List<ByteDifference>();
        long? runStart = null;
        var length = Math.Max(left.Length, right.Length);
        for (var offset = 0; offset < length; offset++)
        {
            // Past the end of the shorter file, every byte of the longer one differs.
            var differs = offset >= left.Length || offset >= right.Length || left[offset] != right[offset];
            if (differs)
            {
                runStart ??= offset;
            }
            else if (runStart is { } start)
            {
                runs.Add(new ByteDifference(start, offset - start));
                runStart = null;
            }
        }

        if (runStart is { } last)
        {
            runs.Add(new ByteDifference(last, length - last));
        }

        return new BinaryDiffResult(left.Length, right.Length, runs);
    }
}
