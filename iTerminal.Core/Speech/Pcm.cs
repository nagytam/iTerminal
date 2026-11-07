namespace iTerminal.Core.Speech;

/// <summary>The recording format Whisper wants and the helpers around it.</summary>
public static class Pcm
{
    public const int SampleRate = 16000;
    public const int BytesPerSecond = SampleRate * 2;

    /// <summary>A recording stops here: 60 seconds, 1.92 MB.</summary>
    public const int MaxSeconds = 60;

    public const int MaxBytes = BytesPerSecond * MaxSeconds;

    /// <summary>Shorter than this (0.3 s) is a key bounce, not speech.</summary>
    public const int MinBytes = BytesPerSecond * 3 / 10;

    /// <summary>Signed 16-bit little-endian samples to floats: -32768 is -1.0 and 32767 is 32767/32768.</summary>
    public static float[] ToSamples(ReadOnlySpan<byte> pcm)
    {
        float[] samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(pcm[2 * i] | (pcm[(2 * i) + 1] << 8)) / 32768f;
        }

        return samples;
    }
}
