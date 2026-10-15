using iConsole.Controls;
using iConsole.Data;
using iConsole.Drivers;
using System.Diagnostics;

namespace iConsole.Layouts;

public static class Layout
{
    public static void Rect(IControl control, Position position, Size size)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);
        Debug.Assert(size.Width < 1000);
        Debug.Assert(size.Height < 1000);

        control.Position = position;
        control.ViewSize = size;
    }

    public static void Position(IControl control, Position position)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);

        control.Position = position;
    }

    public static void LeftBottom(IControl control)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);

        control.Position = new Position(
            parent.Position.X,
            parent.ViewSize.Height - control.ViewSize.Height);
    }

    public static void Fill(IConsoleDriver consoleDriver, IControl control)
    {
        if (control.Parent is null)
        {
            control.Position = new Position(0, 0);
            control.ViewSize = consoleDriver.WindowSize;
        }
    }

    public static void Left(IControl control, IControl relativeTo)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);

        control.Position = new Position(
            relativeTo.Position.X - control.ViewSize.Width,
            relativeTo.Position.Y);
    }

    public static void Bottom(IControl control, IControl relativeTo)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);

        control.Position = new Position(
            relativeTo.Position.X,
            relativeTo.Position.Y + relativeTo.ViewSize.Height);
    }

    public static void Right(IControl control, IControl relativeTo)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);

        control.Position = new Position(
            relativeTo.Position.X + relativeTo.ViewSize.Width,
            relativeTo.Position.Y);
    }

    public static void PadRight(IControl control)
    {
        var parent = control.Parent;
        ArgumentNullException.ThrowIfNull(parent);

        control.Position = new Position(
            parent.ViewSize.Width - control.ViewSize.Width + 1,
            0);
    }

    public static void StackHorizontal(IEnumerable<IControl> controls)
    {
        var position = new Position(0, 0);
        foreach (var control in controls)
        {
            var parent = control.Parent;
            ArgumentNullException.ThrowIfNull(parent);

            control.Position = new Position(position.X, position.Y);
            position.X += control.ViewSize.Width;
        }
    }

    public static void StackVertical(IEnumerable<IControl> controls, uint startX = 0, uint startY = 0)
    {
        var position = new Position(startX, startY);
        foreach (var control in controls)
        {
            var parent = control.Parent;
            ArgumentNullException.ThrowIfNull(parent);

            control.Position = new Position(position.X, position.Y);
            position.Y += control.ViewSize.Height;
        }
    }

    public static void Center(IControl control)
    {
        if (control.Parent is not null)
        {
            control.Position = new Position(
                Math.Max(0, (control.Parent.ViewSize.Width - control.ViewSize.Width) / 2),
                Math.Max(0, (control.Parent.ViewSize.Height - control.ViewSize.Height) / 2));
        }
    }
}
