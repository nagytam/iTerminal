namespace iTerminal.Core.Services.Evidence;

/// <summary>
/// Wraps raw PCM in a canonical 44-byte RIFF/WAVE header, so a voice entry is a file any player opens.
/// Pure: no device, no file, so it is tested directly.
/// </summary>
public static class WavFile
{
    private const int HeaderLength = 44;
    private const int FormatChunkLength = 16;

    /// <summary>The WAV file for <paramref name="pcm"/>: signed 16-bit little-endian samples at <paramref name="sampleRate"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate or channel count is not positive.</exception>
    public static byte[] Build(ReadOnlySpan<byte> pcm, int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        // An int length always fits the RIFF size field, which is an unsigned 32-bit number.
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        var bytes = new byte[HeaderLength + pcm.Length];
        using (var stream = new MemoryStream(bytes))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8);
            writer.Write((uint)(HeaderLength - 8 + pcm.Length));
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write((uint)FormatChunkLength);
            writer.Write((ushort)1);
            writer.Write((ushort)channels);
            writer.Write((uint)sampleRate);
            writer.Write((uint)byteRate);
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)bitsPerSample);
            writer.Write("data"u8);
            writer.Write((uint)pcm.Length);
            writer.Write(pcm);
        }

        return bytes;
    }
}
