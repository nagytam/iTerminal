using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Services;

public sealed class PopoverService : IPopoverService
{
    private readonly Lock _gate = new();
    private Popup? _current;

    public Popup? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsOpen => Current is { IsOpen: true };

    public Task ShowAtPointAsync(Popup popover, IControl owner, Position point)
    {
        ArgumentNullException.ThrowIfNull(popover);
        ArgumentNullException.ThrowIfNull(owner);
        Position position = PopoverPlacement.PlaceAtPoint(point, popover.ViewSize, owner.GetRootControl().ViewSize);
        return ShowAsync(popover, owner, position);
    }

    public Task ShowBesideAsync(Popup popover, IControl owner, IControl target, PopoverSide side = PopoverSide.Below)
    {
        ArgumentNullException.ThrowIfNull(popover);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(target);
        Position position = PopoverPlacement.Place(
            new Rectangle(target.AbsolutePosition, target.ViewSize), popover.ViewSize, owner.GetRootControl().ViewSize, side);
        return ShowAsync(popover, owner, position);
    }

    public void CloseCurrent()
    {
        Popup? popover;
        lock (_gate)
        {
            popover = _current;
            _current = null;
        }

        popover?.Close();
    }

    private Task ShowAsync(Popup popover, IControl owner, Position position)
    {
        Popup? previous;
        lock (_gate)
        {
            previous = _current;
            _current = popover;
        }

        if (!ReferenceEquals(previous, popover))
        {
            previous?.Close();
        }

        popover.Closed -= OnClosed;
        popover.Closed += OnClosed;
        return popover.ShowAtAsync(owner, position);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, sender))
            {
                _current = null;
            }
        }
    }
}
