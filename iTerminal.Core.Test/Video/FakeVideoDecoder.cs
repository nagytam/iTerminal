using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

/// <summary>
/// A decoder that makes up its pictures: a horizontal gradient whose brightness is shifted by the
/// frame number, so two frames are never equal. It ends after <see cref="FrameCount"/> frames.
/// </summary>
internal sealed class FakeVideoDecoder(VideoInfo info, int frameCount = 1000) : IVideoDecoder
{
    private readonly object _lock = new();
    private int _columns;
    private int _rows;
    private int _delivered;
    private bool _disposed;

    public VideoInfo Info { get; } = info;

    public int FrameCount { get; } = frameCount;

    public List<(TimeSpan Offset, int Columns, int Rows)> Opens { get; } = [];

    public void Open(TimeSpan offset, int columns, int rows)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Opens.Add((offset, columns, rows));
            _columns = columns;
            _rows = rows;
            _delivered = 0;
        }
    }

    public bool ReadFrame(byte[] rgb)
    {
        lock (_lock)
        {
            if (_disposed || _delivered >= FrameCount || rgb.Length != _columns * _rows * 3)
            {
                return false;
            }

            for (var i = 0; i < _columns * _rows; i++)
            {
                var value = (byte)(((i % _columns) * 255 / Math.Max(1, _columns - 1) + _delivered) % 256);
                rgb[i * 3] = value;
                rgb[i * 3 + 1] = value;
                rgb[i * 3 + 2] = value;
            }

            _delivered++;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
    }
}

internal sealed class FakeVideoDecoderFactory(Func<IVideoDecoder> create, bool isAvailable = true) : IVideoDecoderFactory
{
    public bool IsAvailable { get; } = isAvailable;

    public IVideoDecoder Create(string path) => create();
}
