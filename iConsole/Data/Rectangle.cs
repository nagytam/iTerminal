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

    public void Include(Rectangle rectangle) =>
        Include(rectangle.Left, rectangle.Top, (int)rectangle.Size.Width, (int)rectangle.Size.Height);

    /// <summary>Grows this rectangle to cover the given one, without making a rectangle of it first: a layer calls this for every row written.</summary>
    public void Include(int left, int top, int width, int height)
    {
        int right = left + width;
        int bottom = top + height;
        if (Left > left)
        {
            var newWidth = (int)Size.Width + (Left - left);
            Position.X = left;
            Size.Width = newWidth;
        }
        if (Top > top)
        {
            var newHeight = (int)Size.Height + (Top - top);
            Position.Y = top;
            Size.Height = newHeight;
        }
        if (Right < right)
        {
            var newWidth = (int)Size.Width + (right - Right);
            Size.Width = newWidth;
        }
        if (Bottom < bottom)
        {
            var newHeight = (int)Size.Height + (bottom - Bottom);
            Size.Height = newHeight;
        }
    }

    public override string ToString()
    {
        return $"({Left},{Top})[{Size.Width},{Size.Height}]";
    }
}
