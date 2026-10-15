namespace iTerminal.Core.Audio;

/// <summary>
/// Turns an encoded audio file into a stream of signed 16-bit little-endian PCM. Every decoder
/// normalises to <see cref="PcmFormat"/> so sinks never deal with sample-width or float conversion.
///
/// <para>Implementations are used from the playback pump thread only and need no internal locking.
/// They must not throw from <see cref="Read"/> after a successful open: a broken file mid-stream is
/// reported as end-of-stream (0 bytes) so playback advances to the next track instead of crashing
/// the pump.</para>
/// </summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>Output format of the PCM produced by <see cref="Read"/>.</summary>
    PcmFormat Format { get; }

    /// <summary>Total playing time, or <see cref="TimeSpan.Zero"/> when the source cannot state it.</summary>
    TimeSpan Duration { get; }

    /// <summary>Whether <see cref="TrySeek"/> can reposition this source.</summary>
    bool CanSeek { get; }

    /// <summary>Playing time corresponding to the data returned by the next <see cref="Read"/>.</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Fills <paramref name="buffer"/> with decoded PCM and returns the number of bytes written,
    /// always a whole number of frames. Returns 0 at end of stream.
    /// </summary>
    int Read(Span<byte> buffer);

    /// <summary>Repositions the source. Returns false when seeking is unsupported or failed.</summary>
    bool TrySeek(TimeSpan position);
}
