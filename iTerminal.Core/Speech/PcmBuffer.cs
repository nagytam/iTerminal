namespace iTerminal.Core.Speech;

/// <summary>A recording's bytes, thread-safe and never larger than <see cref="Pcm.MaxBytes"/>: what does not fit is dropped.</summary>
internal sealed class PcmBuffer
{
    private readonly object _gate = new();
    private readonly MemoryStream _bytes = new();

    public void Append(ReadOnlySpan<byte> chunk)
    {
        lock (_gate)
        {
            int room = Pcm.MaxBytes - (int)_bytes.Length;
            if (room > 0)
            {
                _bytes.Write(chunk[..Math.Min(room, chunk.Length)]);
            }
        }
    }

    public byte[] ToArray()
    {
        lock (_gate)
        {
            return _bytes.ToArray();
        }
    }
}
