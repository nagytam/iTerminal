using iConsole.Data;

namespace iConsole.Controls;

/// <summary>Which side of its target a popover prefers to open on.</summary>
public enum PopoverSide
{
    Below,
    Above,
    Right,
    Left,
}

/// <summary>
/// Works out where a popover goes so it stays on screen without covering what it points at. <see cref="Popup"/> only
/// pulls an overlay back inside the screen, which leaves it on top of its target; these turn it to the other side first.
/// </summary>
public static class PopoverPlacement
{
    /// <summary>
    /// Where a popover of <paramref name="size"/> goes beside <paramref name="target"/> on a screen of
    /// <paramref name="screen"/>. The preferred side is used when the popover fits there, then the opposite side, then
    /// whichever has more room; along the other axis it lines up with the target's start and is pulled back on screen.
    /// </summary>
    public static Position Place(Rectangle target, Size size, Size screen, PopoverSide preferred)
    {
        long width = size.Width;
        long height = size.Height;
        long x;
        long y;

        if (preferred is PopoverSide.Below or PopoverSide.Above)
        {
            long roomBelow = screen.Height - target.Bottom;
            long roomAbove = target.Top;
            bool below = Choose(preferred == PopoverSide.Below, height, roomBelow, roomAbove);
            y = below ? target.Bottom : target.Top - height;
            x = target.Left;
        }
        else
        {
            long roomRight = screen.Width - target.Right;
            long roomLeft = target.Left;
            bool right = Choose(preferred == PopoverSide.Right, width, roomRight, roomLeft);
            x = right ? target.Right : target.Left - width;
            y = target.Top;
        }

        return OnScreen(x, y, size, screen);
    }

    /// <summary>
    /// Where a popover of <paramref name="size"/> goes when it opens at a point, as a context menu does at the pointer: its
    /// top-left corner on the point, or its right and bottom edges on it where it would run off the screen.
    /// </summary>
    public static Position PlaceAtPoint(Position point, Size size, Size screen)
    {
        long x = point.X + size.Width > screen.Width ? point.X - size.Width : point.X;
        long y = point.Y + size.Height > screen.Height ? point.Y - size.Height : point.Y;
        return OnScreen(x, y, size, screen);
    }

    private static bool Choose(bool preferFirst, long needed, long roomFirst, long roomSecond)
    {
        long roomPreferred = preferFirst ? roomFirst : roomSecond;
        long roomOther = preferFirst ? roomSecond : roomFirst;
        if (needed <= roomPreferred)
        {
            return preferFirst;
        }

        if (needed <= roomOther)
        {
            return !preferFirst;
        }

        return roomFirst >= roomSecond;
    }

    private static Position OnScreen(long x, long y, Size size, Size screen)
    {
        long maxX = Math.Max(0, screen.Width - size.Width);
        long maxY = Math.Max(0, screen.Height - size.Height);
        return new Position(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }
}
