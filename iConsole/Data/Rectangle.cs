using System.Diagnostics;

namespace iConsole.Data;

[DebuggerDisplay("Position = {Position}, Size = {Size}")]
public class Rectangle
{
    public Position Position { get; set; }  
    public Size Size { get; set; }
    public int Top => (int)Position.Y;
    public int Bottom => (int)Position.Y + (int)Size.Height;
    public int Left => (int)Position.X;
    public int Right => (int)Position.X + (int)Size.Width;

    public Rectangle(Position position, Size size)
    {
        Position = position;
        Size = size;
    }

    public Rectangle(int left, int top, int width, int height)
    {
        Position = new Position(left, top);
        Size = new Size(width, height);
    }

    public Rectangle(uint x, uint y, uint width, uint height)
    {
        Position = new Position(x, y);
        Size = new Size(width, height);
    }

    public bool Contains(Position position)
    {
        return (Position.X <= position.X) && (Position.X + Size.Width >= position.X) &&
               (Position.Y <= position.Y) && (Position.Y + Size.Height >= position.Y);
    }

    public void Crop(Position position, Size size)
    {
        var positionX = Math.Max(Position.X, position.X);
        var positionY = Math.Max(Position.Y, position.Y);
        var width = Math.Min(Position.X + Size.Width, position.X + size.Width) - positionX;
        var height = Math.Min(Position.Y + Size.Height, position.Y + size.Height) - positionY;
        Position.X = positionX;
        Position.Y = positionY;
        Size.Width = width;
        Size.Height = height;
    }

    public void Crop(Rectangle rect)
    {
        Crop(rect.Position, rect.Size);
    }

    public Rectangle Crop(int left, int top, long width, long height)
    {
        var position = new Position(
            Math.Max(left, Position.X),
            Math.Max(top, Position.Y));
        var size = new Size(
                Math.Max(0, Math.Min(left + width, Position.X + Size.Width) - position.X),
                Math.Max(0, Math.Min(top + height, Position.Y + Size.Height) - position.Y)); 
        return new Rectangle(position, size);
    }

    public void Include(Rectangle rectangle)
    {
        if (Left > rectangle.Left)
        {
            var width = (int)Size.Width + (Left - rectangle.Left);
            Position.X = rectangle.Left;
            Size.Width = width;
        }
        if (Top > rectangle.Top)
        {
            var height = (int)Size.Height + (Top - rectangle.Top);
            Position.Y = rectangle.Top;
            Size.Height = height;
        }
        if (Right < rectangle.Right)
        {
            var width = (int)Size.Width + (rectangle.Right - Right);
            Size.Width = width;
        }
        if (Bottom < rectangle.Bottom)
        {
            var height = (int)Size.Height + (rectangle.Bottom - Bottom);
            Size.Height = height;
        }
    }

    public override string ToString()
    {
        return $"({Left},{Top})[{Size.Width},{Size.Height}]";
    }
}
