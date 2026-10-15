namespace iTerminal.Core.Audio;

/// <summary>
/// The interchange format between decoders and sinks: signed 16-bit little-endian interleaved PCM.
/// Fixing the sample width at 16 bits keeps every sink implementation trivial — the decoders absorb
/// all the format variety (8/24/32-bit, float, mono) so the platform layers only ever see s16le.
/// </summary>
public readonly record struct PcmFormat(int SampleRate, int Channels)
{
    public const int BitsPerSample = 16;
    public const int BytesPerSample = BitsPerSample / 8;

    /// <summary>CD quality — the fallback when a decoder cannot state its own format.</summary>
    public static readonly PcmFormat Default = new(44100, 2);

    public int BlockAlign => Channels * BytesPerSample;

    public int BytesPerSecond => SampleRate * BlockAlign;

    public bool IsValid => SampleRate > 0 && Channels is > 0 and <= 8;

    /// <summary>Converts a byte count in this format to the playing time it represents.</summary>
    public TimeSpan BytesToTime(long byteCount) =>
        BytesPerSecond <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)byteCount / BytesPerSecond);

    /// <summary>Converts a playing time to a block-aligned byte offset in this format.</summary>
    public long TimeToBytes(TimeSpan time)
    {
        if (BytesPerSecond <= 0 || time <= TimeSpan.Zero)
        {
            return 0;
        }

        var bytes = (long)(time.TotalSeconds * BytesPerSecond);
        return bytes - (bytes % BlockAlign);
    }
}
