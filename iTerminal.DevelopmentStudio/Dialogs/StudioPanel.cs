using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.DevelopmentStudio.Model;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>Paints a panel's band: its top row, at <paramref name="x"/>,<paramref name="y"/>, <paramref name="width"/> columns wide.</summary>
internal delegate void StudioPanelBandPainter(IBuffer buffer, int x, int y, int width, ColorScheme colorScheme, bool focused);

/// <summary>
/// One panel of the Development studio, as its own component: a band across its top row (its heading, highlighted while
/// the panel has focus) and a body under it that fills the rest. The panel is docked by an <see cref="DockPanel"/> of the
/// dialog, which gives it its size; the panel sizes its body to what the band leaves. The body is the list the panel shows,
/// and the dialog keeps filling it. The band can be painted by the dialog instead (the output panel's tab strip).
/// </summary>
internal sealed class StudioPanel : Control
{
    public StudioPanel(StudioPanelKind kind, IControl body, Func<string> heading, IThemeService themeService)
        : base(themeService)
    {
        Kind = kind;
        Body = body;
        Heading = heading;
        CanFocus = false;
        AddControl(body);
    }

    /// <summary>Which panel this is. The dialog docks, moves and resizes it by this kind.</summary>
    public StudioPanelKind Kind { get; }

    /// <summary>The list the panel shows. It takes the keys while it has focus.</summary>
    public IControl Body { get; }

    /// <summary>The band's text, read on every draw so a changed cursor or mode shows at once.</summary>
    public Func<string> Heading { get; }

    /// <summary>Paints the band instead of the heading, when the band holds something more than a title (the output tabs).</summary>
    public StudioPanelBandPainter? BandPainter { get; init; }

    /// <summary>Whether the panel's body has the focus, which is when its band is highlighted.</summary>
    public bool HasFocus => Body.IsFocused;

    /// <summary>
    /// Sizes the body to the rows under the band. The dock sets the panel's own size, so the dialog calls this after each
    /// layout; a list that has not been sized yet cannot move its cursor, so a key pressed before the first draw would do nothing.
    /// </summary>
    public void LayoutBody()
    {
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        Body.Position = new Position(0, 1);
        Body.ViewSize = new Size(Math.Max(0, width), Math.Max(0, height - 1));
    }

    public override void Draw(IBuffer buffer)
    {
        if (!IsVisible || ViewSize.IsEmpty) return;

        int width = (int)ViewSize.Width;
        LayoutBody();

        var colorScheme = ThemeService.CurrentColorScheme;
        int x = (int)AbsolutePosition.X;
        int y = (int)AbsolutePosition.Y;
        if (BandPainter is null)
        {
            WriteBand(buffer, x, y, width, " " + Heading(), HasFocus, colorScheme);
        }
        else
        {
            BandPainter(buffer, x, y, width, colorScheme, HasFocus);
        }

        base.Draw(buffer);
    }

    /// <summary>A band is a single line of text: the focused one is drawn in the selection colours, the others on the quiet band.</summary>
    internal void WriteBand(IBuffer buffer, int x, int y, int width, string text, bool focused, ColorScheme colorScheme)
    {
        if (width <= 0) return;

        var foreground = focused ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
        var background = focused ? colorScheme.SelectedFocusedBackground : colorScheme.ButtonBackground;
        string line = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(ZOrder, new Position(x, y), new Size(width, 1), new ConxelString(line, foreground, background));
    }
}
