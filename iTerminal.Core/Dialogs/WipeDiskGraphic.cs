using System.Text;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The running wipe's picture of the disk: one row of cells, overwritten cells solid, the cells still
/// to do light, and a head at the boundary that flickers as it works. A braille spinner in front turns
/// while the wipe runs and becomes a check mark when the row is full. The animation is a pure function
/// of the progress and the clock, so the dialog only has to ask for a new frame on idle.
/// </summary>
public static class WipeDiskGraphic
{
    private static readonly string[] Spin = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private const char Overwritten = '█';
    private const char Untouched = '░';
    private static readonly char[] Head = ['▓', '▒'];

    /// <summary>How long the spinner rests on each frame.</summary>
    public const long SpinMilliseconds = 120;

    /// <summary>How long the head holds each glyph.</summary>
    public const long HeadMilliseconds = 150;

    /// <summary>One line: a spinner, then <paramref name="cells"/> cells filled to <paramref name="fraction"/>.</summary>
    /// <param name="fraction">Bytes done over bytes total, from 0 to 1 (anything else is clamped).</param>
    /// <param name="cells">How many cells the row has; at least one.</param>
    /// <param name="tick">The clock in milliseconds, which drives the spinner and the head.</param>
    public static string Render(double fraction, int cells, long tick)
    {
        cells = Math.Max(1, cells);
        fraction = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        var filled = (int)Math.Floor(fraction * cells);
        var full = filled >= cells;

        var bar = new StringBuilder(cells);
        var headGlyph = Head[(tick / HeadMilliseconds) % Head.Length];
        for (var cell = 0; cell < cells; cell++)
        {
            if (cell < filled) bar.Append(Overwritten);
            else if (cell == filled) bar.Append(headGlyph);
            else bar.Append(Untouched);
        }

        var spinner = full ? "✓" : Spin[(tick / SpinMilliseconds) % Spin.Length];
        return spinner + " [" + bar + "]";
    }
}
