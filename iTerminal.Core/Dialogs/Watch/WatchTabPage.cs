using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// A page of the Watch dialog. A <see cref="TabPage"/> cannot hold the focus itself, and <see cref="Tab"/> only hands a page
/// its keys while one of its children has the focus, so every page owns a focusable surface that fills it. The surface draws
/// the page through <see cref="PaintPage"/>: the page writes its own rows, the digits and the clock faces included, so nothing
/// is drawn by a child control whose layer could be overdrawn by the surface.
/// </summary>
public abstract class WatchTabPage : TabPage
{
    private readonly PageSurface _surface;

    protected WatchTabPage(string name, IThemeService themeService)
        : base(name, themeService)
    {
        _surface = new PageSurface(this, themeService) { CanFocus = true };
        AddControl(_surface);
    }

    /// <summary>The key hints the dialog's footer shows while this page is up.</summary>
    public abstract override IEnumerable<(string Key, string Description)> GetKeyHints();

    /// <summary>
    /// True while a typed entry or a y/n question is open. Esc then cancels that rather than closing the dialog, and the
    /// arrows stay with the page rather than switching tabs under the text being typed.
    /// </summary>
    public virtual bool HasPendingInput => false;

    public virtual void CancelPendingInput()
    {
    }

    public override void OnInitializeLayout()
    {
        _surface.Position = new Position(0, 0);
        _surface.ViewSize = ViewSize;
        LayoutPage(ViewSize);
        base.OnInitializeLayout();
    }

    /// <summary>Places the page's children for a page of <paramref name="size"/>. Called whenever the page is laid out.</summary>
    protected virtual void LayoutPage(Size size)
    {
    }

    /// <summary>Draws the page's rows. Every row in the page's rectangle has to be written, because nothing clears a layer between frames.</summary>
    protected abstract void PaintPage(IBuffer buffer);

    /// <summary>Writes one row of the page, padded to its width, with the given colours.</summary>
    protected void WriteRow(IBuffer buffer, int row, string text, Color foreground, Color background)
    {
        int width = (int)ViewSize.Width;
        if (width <= 0 || row < 0 || row >= (int)ViewSize.Height)
        {
            return;
        }

        buffer.Write(
            _surface.ZOrder,
            new Position(_surface.AbsolutePosition.X, _surface.AbsolutePosition.Y + row),
            new Size(width, 1),
            new ConxelString(text.Cut(width).ExpandRight(width), foreground, background));
    }

    /// <summary>Returns the page's own colours for a row, the selected-and-focused ones when <paramref name="highlighted"/>.</summary>
    protected (Color Foreground, Color Background) RowColours(bool highlighted)
    {
        var colourScheme = ThemeService.CurrentColorScheme;
        if (highlighted && IsFocused)
        {
            return (colourScheme.SelectedFocusedForeground, colourScheme.SelectedBackground);
        }

        return (colourScheme.Foreground, colourScheme.Background);
    }

    protected Color DimColour => ThemeService.CurrentColorScheme.DisabledForeground;

    protected Color ErrorColour => Color.Red;

    /// <summary>The page's surface draws the page; this is the one place the page's rows are written.</summary>
    private sealed class PageSurface(WatchTabPage page, IThemeService themeService) : Control(themeService)
    {
        public override void Draw(IBuffer buffer)
        {
            base.Draw(buffer);
            if (ViewSize.IsEmpty)
            {
                return;
            }

            page.PaintPage(buffer);
        }
    }
}
