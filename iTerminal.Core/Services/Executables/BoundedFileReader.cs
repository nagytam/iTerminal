using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace iTerminal.Core.Services.Executables;

/// <summary>
/// Reads a file at absolute offsets, and never beyond its end. A hostile executable chooses every offset and size it holds, so
/// each read here checks both against the file, caps what it allocates, and answers "no" instead of throwing.
/// </summary>
internal sealed class BoundedFileReader : IDisposable
{
    /// <summary>The most one section's entropy reads; a larger section is sampled from its start.</summary>
    public const long MaxEntropyBytesPerSection = 32L * 1024 * 1024;

    /// <summary>The most all the entropy reads of one file add up to.</summary>
    public const long MaxEntropyBytesTotal = 256L * 1024 * 1024;

    private const int ChunkSize = 64 * 1024;

    private readonly SafeFileHandle _handle;
    private long _entropyBudget = MaxEntropyBytesTotal;

    public BoundedFileReader(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    public long Length { get; }

    /// <summary>True when [offset, offset + count) lies inside the file.</summary>
    public bool InRange(long offset, long count) => offset >= 0 && count >= 0 && offset <= Length && count <= Length - offset;

    /// <summary>Fills the buffer from the offset; false when that would run past the end of the file.</summary>
    public bool TryRead(long offset, Span<byte> buffer)
    {
        if (!InRange(offset, buffer.Length))
        {
            return false;
        }

        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(_handle, buffer[total..], offset + total);
            if (read <= 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    /// <summary>Reads a block, or returns null when it is out of range or larger than <paramref name="max"/>.</summary>
    public byte[]? ReadBytes(long offset, long count, int max)
    {
        if (count > max || !InRange(offset, count))
        {
            return null;
        }

        byte[] buffer = new byte[count];
        return TryRead(offset, buffer) ? buffer : null;
    }

    /// <summary>A NUL-terminated ASCII or UTF-8 string of at most <paramref name="max"/> bytes; null when out of range.</summary>
    public string? ReadCString(long offset, int max = 256)
    {
        if (offset < 0 || offset >= Length)
        {
            return null;
        }

        int length = (int)Math.Min(max, Length - offset);
        byte[] buffer = new byte[length];
        if (!TryRead(offset, buffer))
        {
            return null;
        }

        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0)
        {
            end = buffer.Length;
        }

        return Printable(Encoding.UTF8.GetString(buffer, 0, end));
    }

    /// <summary>Keeps a name safe to put on the screen: control characters become '?'.</summary>
    public static string Printable(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        return new string([.. text.Select(c => char.IsControl(c) ? '?' : c)]);
    }

    /// <summary>
    /// The Shannon entropy, in bits per byte, of a block of the file. Reads in chunks, checks cancellation between them, and stops at the
    /// per-section and per-file caps. Null when nothing could be read.
    /// </summary>
    public (double? Entropy, bool Sampled) Entropy(long offset, long size, CancellationToken cancellationToken)
    {
        if (size <= 0 || offset < 0 || offset >= Length)
        {
            return (null, false);
        }

        long available = Math.Min(size, Length - offset);
        long wanted = Math.Min(Math.Min(available, MaxEntropyBytesPerSection), _entropyBudget);
        if (wanted <= 0)
        {
            return (null, true);
        }

        long[] counts = new long[256];
        byte[] chunk = new byte[ChunkSize];
        long done = 0;
        while (done < wanted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int take = (int)Math.Min(ChunkSize, wanted - done);
            if (!TryRead(offset + done, chunk.AsSpan(0, take)))
            {
                break;
            }

            for (int i = 0; i < take; i++)
            {
                counts[chunk[i]]++;
            }

            done += take;
        }

        _entropyBudget -= done;
        if (done == 0)
        {
            return (null, false);
        }

        double entropy = 0;
        foreach (long count in counts)
        {
            if (count > 0)
            {
                double p = (double)count / done;
                entropy -= p * Math.Log2(p);
            }
        }

        // Sampled when the section is bigger than what went in, or was cut short only by the file's own end (not a sample).
        return (entropy, done < size && done < available);
    }

    /// <summary>True when the block contains the ASCII text; reads at most <paramref name="max"/> bytes of it.</summary>
    public bool Contains(long offset, long length, string ascii, int max = 4 * 1024 * 1024)
    {
        byte[]? block = ReadBytes(offset, Math.Min(length, Math.Min(max, Math.Max(0, Length - offset))), max);
        return block is not null && block.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;
    }

    /// <summary>
    /// True when the block holds any of the ASCII names. Reads in 1 MB chunks that overlap by the longest name, up to
    /// <paramref name="max"/> bytes, so a large string table is searched whole without being held in memory.
    /// </summary>
    public bool ContainsAny(long offset, long length, string[] names, long max = 64L * 1024 * 1024)
    {
        if (!InRange(offset, 0))
        {
            return false;
        }

        byte[][] needles = [.. names.Select(Encoding.ASCII.GetBytes)];
        int overlap = needles.Max(n => n.Length) - 1;
        long end = offset + Math.Min(Math.Min(length, max), Length - offset);
        byte[] buffer = new byte[(int)Math.Min(1024 * 1024, end - offset) + overlap];
        long position = offset;
        while (position < end)
        {
            int take = (int)Math.Min(buffer.Length, end - position);
            if (!TryRead(position, buffer.AsSpan(0, take)))
            {
                return false;
            }

            foreach (byte[] needle in needles)
            {
                if (buffer.AsSpan(0, take).IndexOf(needle) >= 0)
                {
                    return true;
                }
            }

            if (position + take >= end)
            {
                break;
            }

            position += take - overlap;
        }

        return false;
    }

    public static ushort U16(ReadOnlySpan<byte> data, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    public static uint U32(ReadOnlySpan<byte> data, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static ulong U64(ReadOnlySpan<byte> data, int offset, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(data[offset..]) : BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);

    public void Dispose() => _handle.Dispose();
}
