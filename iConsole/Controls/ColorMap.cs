using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public class ColorMap : Control
{
    public ColorMap(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(16, 2);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var line1 = new ConxelString();
        for (int i = 0; i < 16; i++)
        {
            line1.Append(new Conxel('*', (Color)i, Color.Black));
        }
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line1);
        var line2 = new ConxelString();
        for (int i = 0; i < 16; i++)
        {
            line2.Append(new Conxel('*', Color.White, (Color)i));
        }
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y+1), ViewSize, line2);
    }
}
