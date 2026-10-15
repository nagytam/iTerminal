using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Helper;
using System.Diagnostics;

namespace iConsole.Buffers;

public class Buffer : IBuffer
{
    private IConsoleDriver _consoleDriver { get; set; }
    public Lock SyncLock { get; } = new();
    public Size Size { get; set; } = new Size(0, 0);
    public Color DefaultForegroundColor { get; set; }
    public Color DefaultBackgroundColor { get; set; }
    public Cursor Cursor { get; set; }
    public ConcurrentList<BufferLayer> Layers { get; private set; } = [];
    private BufferLayer[] _layersByZOrder = [];
    private BufferLayer? _mergedLayer;
    public BufferLayer MergedLayer 
    {
        get
        {
            if (_mergedLayer == null)
            {
                AddMergedLayer();
            }
            return _mergedLayer;
        }
    }

    public Buffer(IConsoleDriver consoleDriver)
    {
        _consoleDriver = consoleDriver ?? Services.NullConsoleDriver.Instance;
        _ = AddLayer(0);
        Cursor = new(Size);
    }

    private void AddMergedLayer()
    {
        Size = _consoleDriver.WindowSize;
        DefaultForegroundColor = Color.White;
        DefaultBackgroundColor = Color.Black;
        _mergedLayer = new BufferLayer(0, Size, DefaultForegroundColor, DefaultBackgroundColor);
    }

    private BufferLayer AddLayer(int zOrder)
    {
        Size = _consoleDriver.WindowSize;
        DefaultForegroundColor = Color.White;
        DefaultBackgroundColor = Color.Black;
        BufferLayer layer = new(zOrder, Size, DefaultForegroundColor, DefaultBackgroundColor);
        Layers.Add(layer);
        if (_layersByZOrder.Length <= zOrder)
        {
            Array.Resize(ref _layersByZOrder, zOrder + 1);
        }
        _layersByZOrder[zOrder] = layer;
        return layer;
    }

    public IEnumerable<int> UpdateLayers(int maxZOrder)
    {
        List<int> layersToRefresh = [];
        var layersToRemove = Layers.AsSnapshot().Where(l => l.ZOrder > maxZOrder);
        foreach (var layer in layersToRemove)
        {
            Layers.Remove(layer);
        }
        if (_layersByZOrder.Length > maxZOrder + 1)
        {
            Array.Resize(ref _layersByZOrder, maxZOrder + 1);
        }
        for (int i = 0; i <= maxZOrder; i++)
        {
            if (!Layers.Any(l => l.ZOrder == i))
            {
                layersToRefresh.Add(i);
                AddLayer(i);
            }
        }
        return layersToRefresh;
    }

    public void Resize(Size size)
    {
        Size = size;
        foreach (var layer in Layers.AsSnapshot())
        {
            layer.Resize(size);
        }
        MergedLayer.Resize(size);
        if (Cursor?.CursorPosition != null)
        {
            Cursor.Size = size;
            // Clamp cursor inside the buffer bounds.
            Cursor.CursorPosition.X = Math.Min(Cursor.CursorPosition.X, Math.Max(0, size.Width - 1));
            Cursor.CursorPosition.Y = Math.Min(Cursor.CursorPosition.Y, Math.Max(0, size.Height - 1));
        }
    }

    public void Clear()
    {
        var snapshot = Layers.AsSnapshot();
        foreach (var layer in snapshot)
        {
            layer.Clear();
        }
        if (_mergedLayer is not null)
        {
            _mergedLayer.Clear();
        }
    }

    public void Write(int zOrder, Position position, Size size, ConxelString conxelString)
    {
        BufferLayer? layer = null;
        if ((uint)zOrder < (uint)_layersByZOrder.Length)
        {
            layer = _layersByZOrder[zOrder];
        }

        if (layer is not null)
        {
            layer.Resize(Size);
            layer.Write(position, size, conxelString);
        }
    }

    public static void Clear(IBuffer buffer, int zOrder, Position position, Size size, ColorScheme colorScheme)
    {
        var line = new ConxelString(new string(' ', (int)size.Width), colorScheme.Foreground, colorScheme.Background);
        for (uint y = 0; y < size.Height; y++)
        {
            buffer.Write(zOrder, new Position(position.X, y + position.Y), new Size(line.Count, 1), line);
        }
    }

    public void SetCursorPosition(Position position, bool isVisible)
    {
        Cursor.CursorPosition = position;
        Cursor.IsCursorVisible = isVisible;
    }

    public void Merge()
    {
        // Take a single snapshot to avoid repeated lock acquisitions and LINQ allocations.
        // Layers are always maintained in ZOrder order (0, 1, …, n) by UpdateLayers().
        var snapshot = Layers.AsSnapshot();
        if (snapshot.Count == 0) return;

        var baseLayer = snapshot[0];

        // Reuse (or lazily create) the merged layer in-place so that
        // ConsoleBufferRendererJob never observes an intermediate blank state.
        if (_mergedLayer == null || _mergedLayer.Size != baseLayer.Size)
        {
            _mergedLayer = new BufferLayer(0, baseLayer.Size, DefaultForegroundColor, DefaultBackgroundColor);
        }
        else
        {
            _mergedLayer.Clear();
        }

        baseLayer.CopyLayer(MergedLayer);
        for (int z = 1; z < snapshot.Count; z++)
        {
            var layer = snapshot[z];
            if (layer is not null && layer.BoundRect is not null)
            {
                //Debug.Assert(layer.BoundRect.Right <= baseLayer.Size.Width);
                //Debug.Assert(layer.BoundRect.Bottom <= baseLayer.Size.Height);
                MergedLayer.Merge(layer, layer.BoundRect);
            }
            else
            {
                // Debugger.Break();
            }
        }
    }
}
