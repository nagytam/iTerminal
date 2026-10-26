namespace iConsole.Data;

/// <summary>
/// A width in cells on each side of a rectangle: the margin outside a border or the padding inside it.
/// Negative values are taken as zero.
/// </summary>
public readonly record struct Thickness
{
    public Thickness(int all) : this(all, all, all, all)
    {
    }

    public Thickness(int horizontal, int vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public Thickness(int left, int top, int right, int bottom)
    {
        Left = Math.Max(0, left);
        Top = Math.Max(0, top);
        Right = Math.Max(0, right);
        Bottom = Math.Max(0, bottom);
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    public static Thickness Zero => default;

    /// <summary>Left plus right.</summary>
    public int Horizontal => Left + Right;

    /// <summary>Top plus bottom.</summary>
    public int Vertical => Top + Bottom;

    /// <summary>The size left inside this thickness, never below zero.</summary>
    public Size Deflate(Size outer)
    {
        return new Size(Math.Max(0, outer.Width - Horizontal), Math.Max(0, outer.Height - Vertical));
    }

    /// <summary>The size needed to hold <paramref name="inner"/> plus this thickness.</summary>
    public Size Inflate(Size inner)
    {
        return new Size(inner.Width + Horizontal, inner.Height + Vertical);
    }

    /// <summary>The sum of two thicknesses, side by side.</summary>
    public static Thickness operator +(Thickness a, Thickness b)
    {
        return new Thickness(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);
    }
}
