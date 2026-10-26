using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public class Label(string text, IThemeService themeService) 
    : Control(themeService ?? NullThemeService.Instance)
{
    public virtual string Text { get; set; } = text;
    public virtual Color ForegroundColor { get; set; } = (themeService ?? NullThemeService.Instance).CurrentColorScheme.Foreground;
    public virtual Color BackgroundColor { get; set; } = (themeService ?? NullThemeService.Instance).CurrentColorScheme.Background;
    public virtual bool IsBold { get; set; } = false;
    public virtual bool IsItalic { get; set; } = false;
    public virtual bool IsUnderline { get; set; } = false;
    public virtual bool IsStrikeThrough { get; set; } = false;
    public virtual bool IsDim { get; set; } = false;
    public virtual bool IsReverse { get; set; } = false;
    public virtual bool IsBlink { get; set; } = false;
    public virtual bool IsOverline { get; set; } = false;

    /// <summary>The line style when <see cref="IsUnderline"/> is set.</summary>
    public virtual UnderlineStyle UnderlineStyle { get; set; } = UnderlineStyle.Single;

    /// <summary>The underline's own colour; null leaves it the colour of the text.</summary>
    public virtual Color? UnderlineColor { get; set; }

    /// <summary>
    /// Where the whole text links to (an OSC 8 hyperlink, which a supporting terminal opens on a Ctrl+click). Not drawn;
    /// set <see cref="IsUnderline"/> as well to make it look like one.
    /// </summary>
    public virtual string? Hyperlink { get; set; }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        // Pad to the full width: nothing clears a layer between frames, so a label whose text got
        // shorter ("Loading…" → "Done") would otherwise keep the tail of the old text on screen.
        var text = (Text ?? string.Empty).PadRight((int)ViewSize.Width);
        var s = new ConxelString(text, ForegroundColor, BackgroundColor, IsBold, IsItalic, IsUnderline, IsStrikeThrough);
        s.Edit(0, s.Count, conxel =>
        {
            conxel.IsDim = IsDim;
            conxel.IsReverse = IsReverse;
            conxel.IsBlink = IsBlink;
            conxel.IsOverline = IsOverline;
            conxel.UnderlineStyle = UnderlineStyle;
            conxel.HasUnderlineColor = UnderlineColor is not null;
            conxel.UnderlineColor = UnderlineColor ?? default;
            return conxel;
        });
        if (!string.IsNullOrEmpty(Hyperlink))
        {
            // Only the text, not the padding after it, is the link.
            s.SetHyperlink(Hyperlink, 0, (Text ?? string.Empty).Length);
        }

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
