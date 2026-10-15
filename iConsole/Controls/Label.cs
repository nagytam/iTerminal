using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public class Label(string text, IThemeService themeService) 
    : Control(themeService)
{
    public virtual string Text { get; set; } = text;
    public virtual Color ForegroundColor { get; set; } = (themeService ?? NullThemeService.Instance).CurrentColorScheme.Foreground;
    public virtual Color BackgroundColor { get; set; } = (themeService ?? NullThemeService.Instance).CurrentColorScheme.Background;
    public virtual bool IsBold { get; set; } = false;
    public virtual bool IsItalic { get; set; } = false;
    public virtual bool IsUnderline { get; set; } = false;
    public virtual bool IsStrikeThrough { get; set; } = false;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        // Pad to the full width: nothing clears a layer between frames, so a label whose text got
        // shorter ("Loading…" → "Done") would otherwise keep the tail of the old text on screen.
        var text = (Text ?? string.Empty).PadRight((int)ViewSize.Width);
        var s = new ConxelString(text, ForegroundColor, BackgroundColor, IsBold, IsItalic, IsUnderline, IsStrikeThrough);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
