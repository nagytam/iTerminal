namespace iConsole.Layouts;

/// <summary>How a <see cref="StackPanel"/> sizes one child along its axis.</summary>
public enum SizeRuleKind
{
    /// <summary>Exactly <see cref="SizeRule.Value"/> cells.</summary>
    Fixed,

    /// <summary><see cref="SizeRule.Value"/> percent of the space the gaps leave.</summary>
    Percent,

    /// <summary>A share of what the fixed and percent children leave, in proportion to <see cref="SizeRule.Value"/> (the weight).</summary>
    Fill,
}

/// <summary>
/// A size given as a rule rather than a cell count: <see cref="Fixed"/>, <see cref="Percent"/> or <see cref="Fill"/>,
/// optionally held between <see cref="Min"/> and <see cref="Max"/> cells.
/// </summary>
public readonly record struct SizeRule(SizeRuleKind Kind, double Value, int Min = 0, int Max = int.MaxValue)
{
    public static SizeRule Fixed(int cells) => new(SizeRuleKind.Fixed, Math.Max(0, cells));

    public static SizeRule Percent(double percent) => new(SizeRuleKind.Percent, Sanitize(percent));

    /// <summary>A share of the leftover space; two children with weights 2 and 1 get a 2:1 split.</summary>
    public static SizeRule Fill(double weight = 1) => new(SizeRuleKind.Fill, Sanitize(weight));

    public SizeRule WithMin(int cells) => this with { Min = Math.Max(0, cells) };

    public SizeRule WithMax(int cells) => this with { Max = Math.Max(0, cells) };

    private static double Sanitize(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, value);
}
