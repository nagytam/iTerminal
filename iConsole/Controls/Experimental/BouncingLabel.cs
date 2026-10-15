using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Experimental;

public class BouncingLabel : Label
{
    private Random _random { get; set; }
    private readonly Lock _lock = new();
    private int bounceX { get; set; } = 0;
    private int bounceY { get; set; } = 0;
    private bool _positionChanged = false;

    public BouncingLabel(Position position, string text, IThemeService themeService)
        : base(text, themeService)
    {
        Position = new Position(position.X, position.Y);
        _random = new Random();
        Task.Run(() => Bounce());
    }

    private void Bounce()
    {
        while (Parent == null)
        {
            Thread.Sleep(10);
        }

        var maxWidth = Parent.ViewSize.Width;
        var maxHeight = Parent.ViewSize.Height;

        bounceX = _random.Next() - int.MaxValue / 2 > 0 ? 1 : -1;
        bounceY = _random.Next() - int.MaxValue / 2 > 0 ? 1 : -1;

        while (true)
        {
            lock (_lock)
            {
                if (Position.X + bounceX < 0 || Position.X + bounceX + Text.Length >= maxWidth ||
                    Position.Y + bounceY < 0 || Position.Y + bounceY >= maxHeight)
                {
                    if (Position.X == 0 || Position.X + Text.Length >= maxWidth)
                    {
                        bounceX = -bounceX;
                    }
                    if (Position.Y == 0 || Position.Y >= maxHeight)
                    {
                        bounceY = -bounceY;
                    }
                }

                var changed = false;
                if (Position.X + bounceX >= 0 && Position.X + bounceX + Text.Length < maxWidth)
                {
                    changed = true;
                    Position.X = (uint)Math.Max(Position.X + bounceX, 0);
                }
                if (Position.Y + bounceY >= 0 && Position.Y + bounceY < maxHeight)
                {
                    changed = true;
                    Position.Y = (uint)Math.Max(Position.Y + bounceY, 0);
                }
                if (changed)
                {
                    _positionChanged = true;
                    StateHasChanged();
                }
            }
            Thread.Sleep(100);
        }
    }

    public override void Draw(IBuffer buffer)
    {
        lock (_lock)
        {
            if (_positionChanged)
            {
                _positionChanged = false;
            }
            var colorScheme = ThemeService.CurrentColorScheme;
            var s = new ConxelString(Text, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
        }
    }
}
