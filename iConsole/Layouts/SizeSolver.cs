namespace iConsole.Layouts;

/// <summary>Turns a list of <see cref="SizeRule"/>s into cell counts for a given length.</summary>
public static class SizeSolver
{
    /// <summary>
    /// The size of each rule along <paramref name="length"/> cells, with <paramref name="gap"/> cells between neighbours.
    /// The sizes plus the gaps never exceed the length: when fixed and percent sizes do not fit, the last children shrink first.
    /// Fill children share exactly what is left (no cell is lost to rounding), within their minimums and maximums.
    /// </summary>
    public static int[] Solve(int length, IReadOnlyList<SizeRule> rules, int gap = 0)
    {
        int count = rules.Count;
        int[] sizes = new int[count];
        if (count == 0)
        {
            return sizes;
        }

        int space = Math.Max(0, length - Math.Max(0, gap) * (count - 1));
        for (int i = 0; i < count; i++)
        {
            SizeRule rule = rules[i];
            if (rule.Kind == SizeRuleKind.Fixed)
            {
                sizes[i] = Limit(rule, (int)Math.Min(rule.Value, int.MaxValue));
            }
            else if (rule.Kind == SizeRuleKind.Percent)
            {
                sizes[i] = Limit(rule, (int)Math.Floor(space * Math.Min(rule.Value, 100) / 100));
            }
        }

        // Too much asked for: take it back from the end, so the first children keep their size.
        int used = 0;
        for (int i = 0; i < count; i++)
        {
            if (rules[i].Kind != SizeRuleKind.Fill)
            {
                sizes[i] = Math.Min(sizes[i], space - used);
                used += sizes[i];
            }
        }

        DistributeFill(space - used, rules, sizes);

        // Minimums that cannot all be met overflow; the last children give the excess back.
        int excess = sizes.Sum() - space;
        for (int i = count - 1; i >= 0 && excess > 0; i--)
        {
            int cut = Math.Min(sizes[i], excess);
            sizes[i] -= cut;
            excess -= cut;
        }

        return sizes;
    }

    private static int Limit(SizeRule rule, int cells) => Math.Clamp(cells, rule.Min, Math.Max(rule.Min, rule.Max));

    private static void DistributeFill(int remaining, IReadOnlyList<SizeRule> rules, int[] sizes)
    {
        List<int> open = [];
        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i].Kind == SizeRuleKind.Fill)
            {
                open.Add(i);
            }
        }

        // Children that hit a minimum or a maximum are fixed at it and the rest share what remains, until none does.
        while (open.Count > 0)
        {
            double weights = open.Sum(i => rules[i].Value);
            double[] shares = open.Select(i => weights > 0 ? remaining * rules[i].Value / weights : (double)remaining / open.Count).ToArray();
            double violation = 0;
            for (int k = 0; k < open.Count; k++)
            {
                violation += Limit(rules[open[k]], (int)Math.Round(shares[k])) - shares[k];
            }

            if (Math.Abs(violation) < 0.5)
            {
                break;
            }

            List<int> frozen = [];
            for (int k = 0; k < open.Count; k++)
            {
                SizeRule rule = rules[open[k]];
                bool tooSmall = shares[k] < rule.Min;
                bool tooBig = shares[k] > rule.Max;
                if ((violation > 0 && tooSmall) || (violation < 0 && tooBig))
                {
                    sizes[open[k]] = Limit(rule, (int)Math.Round(shares[k]));
                    remaining -= sizes[open[k]];
                    frozen.Add(open[k]);
                }
            }

            if (frozen.Count == 0)
            {
                break;
            }

            open.RemoveAll(frozen.Contains);
        }

        remaining = Math.Max(0, remaining);
        if (open.Count == 0)
        {
            return;
        }

        // Whole cells by largest remainder, so the shares add up to exactly what is left.
        double total = open.Sum(i => rules[i].Value);
        double[] exact = open.Select(i => total > 0 ? remaining * rules[i].Value / total : (double)remaining / open.Count).ToArray();
        int assigned = 0;
        for (int k = 0; k < open.Count; k++)
        {
            sizes[open[k]] = (int)Math.Floor(exact[k]);
            assigned += sizes[open[k]];
        }

        foreach (int k in Enumerable.Range(0, open.Count).OrderByDescending(k => exact[k] - Math.Floor(exact[k])).ThenBy(k => k))
        {
            if (assigned >= remaining)
            {
                break;
            }

            sizes[open[k]]++;
            assigned++;
        }
    }
}
