using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Experimental;

/// <summary>
/// A label that drifts diagonally inside its parent and turns round at the edges — a demonstration of a control
/// that changes by itself. A background task moves it every <see cref="StepInterval"/> milliseconds once it has a
/// parent; <see cref="Dispose"/> stops that task. The task is optional (<c>autoStart: false</c>) so that
/// <see cref="Step"/> can be driven by hand.
/// </summary>
public class BouncingLabel : Label, IDisposable
{
    private const int StepInterval = 100;

    private readonly Random _random = new();
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private bool _positionChanged = false;

    public BouncingLabel(Position position, string text, IThemeService themeService, bool autoStart = true)
        : base(text, themeService)
    {
        Position = new Position(position.X, position.Y);
        if (autoStart)
        {
            Task.Run(Bounce);
        }
    }

    /// <summary>Horizontal direction: +1 to the right, -1 to the left.</summary>
    public int BounceX { get; set; }

    /// <summary>Vertical direction: +1 down, -1 up.</summary>
    public int BounceY { get; set; }

    private void Bounce()
    {
        while (Parent == null)
        {
            if (_stop.Token.WaitHandle.WaitOne(10))
            {
                return;
            }
        }

        BounceX = _random.Next(2) == 0 ? -1 : 1;
        BounceY = _random.Next(2) == 0 ? -1 : 1;

        while (!_stop.IsCancellationRequested)
        {
            var parent = Parent;
            if (parent is not null)
            {
                Step((long)parent.ViewSize.Width, (long)parent.ViewSize.Height);
            }

            if (_stop.Token.WaitHandle.WaitOne(StepInterval))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Moves the label one cell along its direction inside a parent <paramref name="maxWidth"/> by
    /// <paramref name="maxHeight"/> cells. A direction that would take the label past an edge — it must keep one cell
    /// clear at the right, as the text is drawn — is reversed first, on whichever axis would leave, so the label
    /// turns round at the edge instead of sticking to it. Returns whether it moved.
    /// </summary>
    public bool Step(long maxWidth, long maxHeight)
    {
        lock (_lock)
        {
            if (BounceX == 0)
            {
                BounceX = 1;
            }

            if (BounceY == 0)
            {
                BounceY = 1;
            }

            long width = Text.Length;
            if (!FitsX(Position.X + BounceX, width, maxWidth))
            {
                BounceX = -BounceX;
            }

            if (!FitsY(Position.Y + BounceY, maxHeight))
            {
                BounceY = -BounceY;
            }

            var changed = false;
            if (FitsX(Position.X + BounceX, width, maxWidth))
            {
                Position.X = Position.X + BounceX;
                changed = true;
            }

            if (FitsY(Position.Y + BounceY, maxHeight))
            {
                Position.Y = Position.Y + BounceY;
                changed = true;
            }

            if (changed)
            {
                _positionChanged = true;
                StateHasChanged();
            }

            return changed;
        }
    }

    private static bool FitsX(long x, long width, long maxWidth) => x >= 0 && x + width < maxWidth;

    private static bool FitsY(long y, long maxHeight) => y >= 0 && y < maxHeight;

    public void Dispose()
    {
        _stop.Cancel();
        GC.SuppressFinalize(this);
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
