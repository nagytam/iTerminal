using iConsole.Data;

namespace iConsole.Services;

public sealed class ToolTipService : IToolTipService
{
    private readonly Lock _gate = new();
    private Position? _pointerPosition;
    private long _version;

    public Position? PointerPosition
    {
        get
        {
            lock (_gate)
            {
                return _pointerPosition;
            }
        }
    }

    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public void ReportPointer(Position position)
    {
        lock (_gate)
        {
            _pointerPosition = new Position(position.X, position.Y);
            _version++;
        }
    }

    public void Dismiss()
    {
        lock (_gate)
        {
            _pointerPosition = null;
            _version++;
        }
    }
}
