using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using System.Diagnostics;

namespace iConsole.Controls.ScrollBar;

public class ScrollBar(IThemeService themeService)
    : Control(themeService)
{
    public IScrollableModel? ScrollableModel { get; set; }
    public Orientation ScrollBarOrientation { get; set; } = Orientation.Vertical;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return; // TODO
        }
        switch (ScrollBarOrientation)
        {
            case Orientation.Vertical:
                {
                    DrawVerticalScrollBar(buffer);
                    break;
                }
            case Orientation.Horizontal:
                {
                    DrawHorizontalScrollBar(buffer);
                    break;
                }
        }
    }

    private void DrawVerticalScrollBar(IBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(ScrollableModel, nameof(ScrollableModel));

        if (ViewSize.Width < 1 || ViewSize.Height < 1)
        {
            return;
        }

        Debug.Assert(1 == ViewSize.Width);

        if (ViewSize.Height < 3)
        {
            var colorSchemeCompact = ThemeService.CurrentColorScheme;
            var compact = new ConxelString(Border.ScrollPosition, colorSchemeCompact.Foreground, colorSchemeCompact.Background);
            buffer.Write(ZOrder, AbsolutePosition, SizeHelper.Size1x1, compact);
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var position = AbsolutePosition;
        var scrollHeight = ViewSize.Height - 3;
        var scrollPosition =
            ScrollableModel.ScrollableSize.Height > 1
            ? (int)(scrollHeight * ScrollableModel.CursorPosition.Y / (ScrollableModel.ScrollableSize.Height - 1))
            : 0;

        var c = new ConxelString(Border.ArrowUp, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, position, SizeHelper.Size1x1, c);
        c = new ConxelString(Border.VerticalSingle, colorScheme.Foreground, colorScheme.Background);
        for (int y = 1; y <= scrollPosition; y++)
        {
            buffer.Write(ZOrder, new Position(position.X, (uint)(position.Y + y)), SizeHelper.Size1x1,
                c);
        }
        c = new ConxelString(Border.ScrollPosition, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(position.X, (uint)(position.Y + scrollPosition + 1)), SizeHelper.Size1x1,
            c);
        c = new ConxelString(Border.VerticalSingle, colorScheme.Foreground, colorScheme.Background);
        for (int y = scrollPosition + 2; y < ViewSize.Height - 1; y++)
        {
            buffer.Write(ZOrder, new Position(position.X, (uint)(position.Y + y)), SizeHelper.Size1x1,
                c);
        }
        c = new ConxelString(Border.ArrowDown, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(position.X, position.Y + ViewSize.Height - 1), SizeHelper.Size1x1,
            c);
    }
    private void DrawHorizontalScrollBar(IBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(ScrollableModel, nameof(ScrollableModel));

        if (ViewSize.Width < 1 || ViewSize.Height < 1)
        {
            return;
        }

        Debug.Assert(1 == ViewSize.Height);

        if (ViewSize.Width < 3)
        {
            var colorSchemeCompact = ThemeService.CurrentColorScheme;
            var compact = new ConxelString(Border.ScrollPosition, colorSchemeCompact.Foreground, colorSchemeCompact.Background);
            buffer.Write(ZOrder, AbsolutePosition, SizeHelper.Size1x1, compact);
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var position = AbsolutePosition;
        var scrollWidth = ViewSize.Width - 3;
        var scrollPosition =
            ScrollableModel.ScrollableSize.Width > 1
            ? (int)(scrollWidth * ScrollableModel.CursorPosition.X / (ScrollableModel.ScrollableSize.Width - 1))
            : 0;

        var c = new ConxelString(Border.ArrowLeft, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, position, SizeHelper.Size1x1,
            c);
        c = new ConxelString(Border.HorizontalSingle, colorScheme.Foreground, colorScheme.Background);
        for (int x = 1; x <= scrollPosition; x++)
        {
            buffer.Write(ZOrder, new Position(position.X + x, position.Y), SizeHelper.Size1x1,
                c);
        }
        c = new ConxelString(Border.ScrollPosition, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(position.X + scrollPosition + 1, position.Y), SizeHelper.Size1x1,
            c);
        c = new ConxelString(Border.HorizontalSingle, colorScheme.Foreground, colorScheme.Background);
        for (int x = scrollPosition + 2; x < ViewSize.Width - 1; x++)
        {
            buffer.Write(ZOrder, new Position(position.X + x, position.Y), SizeHelper.Size1x1,
                c);
        }
        c = new ConxelString(Border.ArrowRight, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(position.X + ViewSize.Width - 1, position.Y), SizeHelper.Size1x1,
            c);
    }
}
