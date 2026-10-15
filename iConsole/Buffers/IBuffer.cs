using iConsole.Data;
using iConsole.Display;

namespace iConsole.Buffers;

public interface IBuffer
{
    Lock SyncLock { get; }
    Size Size { get; }
    Cursor Cursor { get; set; }
    ConcurrentList<BufferLayer> Layers { get; }
    BufferLayer MergedLayer { get; }

    void Clear();
    void Resize(Size newSize);
    void Write(int zOrder, Position position, Size size, ConxelString conxelString);
    void SetCursorPosition(Position position, bool isVisible);
    void Merge();
    IEnumerable<int> UpdateLayers(int maxZOrder);
}