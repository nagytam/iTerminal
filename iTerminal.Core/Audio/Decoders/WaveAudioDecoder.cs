using System.Buffers.Binary;

namespace iTerminal.Core.Audio.Decoders;

/// <summary>
/// RIFF/WAVE reader covering the uncompressed cases that actually show up on disk: integer PCM at
/// 8/16/24/32 bits and IEEE float at 32/64 bits, including WAVE_FORMAT_EXTENSIBLE wrappers. Anything
/// else (ADPCM, MP3-in-WAV, …) is rejected at construction so the factory can fall back to ffmpeg.
///
/// <para>Everything is converted to s16le on the way out; the channel count is passed through
/// untouched, since both sinks handle mono and multi-channel streams natively.</para>
/// </summary>
public sealed class WaveAudioDecoder : IAudioDecoder
{
    private const int FormatPcm = 0x0001;
    private const int FormatIeeeFloat = 0x0003;
    private const int FormatExtensible = 0xFFFE;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly int _formatTag;
    private readonly int _sourceBitsPerSample;
    private readonly int _sourceBlockAlign;
    private readonly long _dataOffset;
    private readonly long _dataLength;

    private long _dataPosition;
    private byte[] _scratch = [];

    public PcmFormat Format { get; }

    public TimeSpan Duration { get; }

    public bool CanSeek => _stream.CanSeek;

    public TimeSpan Position =>
        _sourceBlockAlign <= 0 || Format.SampleRate <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)(_dataPosition / _sourceBlockAlign) / Format.SampleRate);

    public WaveAudioDecoder(string path)
        : this(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), ownsStream: true)
    {
    }

    public WaveAudioDecoder(Stream stream, bool ownsStream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;

        var header = ReadHeader(stream);
        _formatTag = header.FormatTag;
        _sourceBitsPerSample = header.BitsPerSample;
        _sourceBlockAlign = header.BlockAlign;
        _dataOffset = header.DataOffset;
        _dataLength = header.DataLength;

        Format = new PcmFormat(header.SampleRate, header.Channels);
        Duration = _sourceBlockAlign > 0 && header.SampleRate > 0
            ? TimeSpan.FromSeconds((double)(_dataLength / _sourceBlockAlign) / header.SampleRate)
            : TimeSpan.Zero;

        if (stream.CanSeek)
        {
            stream.Position = _dataOffset;
        }
    }

    /// <summary>
    /// Cheap probe used by the decoder factory: true when the file starts with a RIFF/WAVE header
    /// this decoder can actually handle. Never throws.
    /// </summary>
    public static bool CanDecode(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = ReadHeader(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int Read(Span<byte> buffer)
    {
        var outputBlockAlign = Format.BlockAlign;
        if (outputBlockAlign <= 0 || _sourceBlockAlign <= 0)
        {
            return 0;
        }

        var frames = buffer.Length / outputBlockAlign;
        if (frames <= 0)
        {
            return 0;
        }

        var remainingFrames = (_dataLength - _dataPosition) / _sourceBlockAlign;
        if (remainingFrames <= 0)
        {
            return 0;
        }

        frames = (int)Math.Min(frames, remainingFrames);
        var sourceBytes = frames * _sourceBlockAlign;
        if (_scratch.Length < sourceBytes)
        {
            _scratch = new byte[sourceBytes];
        }

        var read = ReadExactly(_stream, _scratch, sourceBytes);
        if (read <= 0)
        {
            return 0;
        }

        read -= read % _sourceBlockAlign;
        _dataPosition += read;

        var sampleCount = read / (_sourceBitsPerSample / 8);
        Convert(_scratch.AsSpan(0, read), buffer, sampleCount);
        return sampleCount * PcmFormat.BytesPerSample;
    }

    public bool TrySeek(TimeSpan position)
    {
        if (!_stream.CanSeek || _sourceBlockAlign <= 0 || Format.SampleRate <= 0)
        {
            return false;
        }

        var frame = (long)(Math.Max(0, position.TotalSeconds) * Format.SampleRate);
        var offset = frame * _sourceBlockAlign;
        offset = Math.Clamp(offset, 0, Math.Max(0, _dataLength));

        _stream.Position = _dataOffset + offset;
        _dataPosition = offset;
        return true;
    }

    private void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int sampleCount)
    {
        switch (_formatTag, _sourceBitsPerSample)
        {
            case (FormatPcm, 8):
                // 8-bit WAV is unsigned with a 128 bias; centre it and scale up to 16-bit.
                for (int i = 0; i < sampleCount; i++)
                {
                    WriteSample(destination, i, (short)((source[i] - 128) << 8));
                }
                break;

            case (FormatPcm, 16):
                source[..(sampleCount * 2)].CopyTo(destination);
                break;

            case (FormatPcm, 24):
                for (int i = 0; i < sampleCount; i++)
                {
                    var offset = i * 3;
                    WriteSample(destination, i, (short)(source[offset + 1] | (source[offset + 2] << 8)));
                }
                break;

            case (FormatPcm, 32):
                for (int i = 0; i < sampleCount; i++)
                {
                    var value = BinaryPrimitives.ReadInt32LittleEndian(source[(i * 4)..]);
                    WriteSample(destination, i, (short)(value >> 16));
                }
                break;

            case (FormatIeeeFloat, 32):
                for (int i = 0; i < sampleCount; i++)
                {
                    var value = BitConverter.ToSingle(source[(i * 4)..]);
                    WriteSample(destination, i, FloatToPcm16(value));
                }
                break;

            case (FormatIeeeFloat, 64):
                for (int i = 0; i < sampleCount; i++)
                {
                    var value = BitConverter.ToDouble(source[(i * 8)..]);
                    WriteSample(destination, i, FloatToPcm16(value));
                }
                break;

            default:
                destination[..(sampleCount * PcmFormat.BytesPerSample)].Clear();
                break;
        }
    }

    /// <summary>Clamped float-to-s16 conversion shared by the float WAV and MP3 paths.</summary>
    internal static short FloatToPcm16(double value)
    {
        var scaled = value * short.MaxValue;
        return scaled switch
        {
            >= short.MaxValue => short.MaxValue,
            <= short.MinValue => short.MinValue,
            _ => (short)scaled,
        };
    }

    private static void WriteSample(Span<byte> destination, int index, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(destination[(index * 2)..], value);

    private static int ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read <= 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    private static WaveHeader ReadHeader(Stream stream)
    {
        Span<byte> chunkHeader = stackalloc byte[8];

        Span<byte> riff = stackalloc byte[12];
        if (ReadExactly(stream, riff) != riff.Length
            || !riff[..4].SequenceEqual("RIFF"u8)
            || !riff[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        int formatTag = 0;
        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        int blockAlign = 0;
        var haveFormat = false;

        while (ReadExactly(stream, chunkHeader) == chunkHeader.Length)
        {
            var chunkId = chunkHeader[..4];
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);

            if (chunkId.SequenceEqual("fmt "u8))
            {
                var fmt = new byte[Math.Max(16, (int)Math.Min(chunkSize, 64))];
                var fmtRead = ReadExactly(stream, fmt, (int)Math.Min(chunkSize, (uint)fmt.Length));
                if (fmtRead < 16)
                {
                    throw new InvalidDataException("Truncated WAVE fmt chunk.");
                }

                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(12));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14));

                // EXTENSIBLE hides the real encoding in the first two bytes of the SubFormat GUID.
                if (formatTag == FormatExtensible && fmtRead >= 40)
                {
                    formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24));
                }

                // Skip any remaining bytes of an oversized fmt chunk.
                var consumed = Math.Min(chunkSize, (uint)fmt.Length);
                Skip(stream, chunkSize - consumed);
                haveFormat = true;
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                if (!haveFormat)
                {
                    throw new InvalidDataException("WAVE data chunk before fmt chunk.");
                }

                if (formatTag is not (FormatPcm or FormatIeeeFloat)
                    || bitsPerSample is not (8 or 16 or 24 or 32 or 64)
                    || (formatTag == FormatIeeeFloat && bitsPerSample is not (32 or 64))
                    || channels <= 0
                    || sampleRate <= 0)
                {
                    throw new InvalidDataException($"Unsupported WAVE format (tag {formatTag}, {bitsPerSample} bit).");
                }

                if (blockAlign <= 0)
                {
                    blockAlign = channels * (bitsPerSample / 8);
                }

                var dataOffset = stream.CanSeek ? stream.Position : 0;
                // A streamed/truncated file may declare a size beyond what is really there, and
                // 0xFFFFFFFF is the conventional "unknown length" marker — clamp to the real file.
                long dataLength = chunkSize;
                if (stream.CanSeek)
                {
                    dataLength = Math.Min(dataLength, stream.Length - dataOffset);
                }
                dataLength -= dataLength % blockAlign;

                return new WaveHeader(formatTag, channels, sampleRate, bitsPerSample, blockAlign, dataOffset, dataLength);
            }
            else
            {
                // RIFF chunks are word-aligned: an odd size is followed by a pad byte.
                Skip(stream, chunkSize + (chunkSize & 1));
            }
        }

        throw new InvalidDataException("WAVE data chunk not found.");
    }

    private static void Skip(Stream stream, long count)
    {
        if (count <= 0)
        {
            return;
        }

        if (stream.CanSeek)
        {
            stream.Position += count;
            return;
        }

        Span<byte> sink = stackalloc byte[1024];
        while (count > 0)
        {
            var read = stream.Read(sink[..(int)Math.Min(count, sink.Length)]);
            if (read <= 0)
            {
                break;
            }
            count -= read;
        }
    }

    private static int ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    public void Dispose()
    {
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }

    private readonly record struct WaveHeader(
        int FormatTag,
        int Channels,
        int SampleRate,
        int BitsPerSample,
        int BlockAlign,
        long DataOffset,
        long DataLength);
}
