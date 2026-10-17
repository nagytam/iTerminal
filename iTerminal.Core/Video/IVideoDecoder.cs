namespace iTerminal.Core.Video;

/// <summary>
/// A source of decoded video frames, already scaled to the character grid the picture will be drawn
/// on. Scaling happens in the decoder (ffmpeg does it far faster than managed code could), so a
/// frame is exactly <c>columns × rows × 3</c> bytes of packed RGB.
/// </summary>
public interface IVideoDecoder : IDisposable
{
    VideoInfo Info { get; }

    /// <summary>(Re)starts decoding at <paramref name="offset"/> with frames of <paramref name="columns"/> × <paramref name="rows"/> pixels.</summary>
    void Open(TimeSpan offset, int columns, int rows);

    /// <summary>Blocks until the next frame is in <paramref name="rgb"/>. False at the end of the stream, or when the decoder was reopened or disposed meanwhile.</summary>
    bool ReadFrame(byte[] rgb);
}

public interface IVideoDecoderFactory
{
    /// <summary>True when this machine can decode video at all (ffmpeg on PATH).</summary>
    bool IsAvailable { get; }

    /// <summary>Opens the file and reads its dimensions. Throws when the file is not a video the machine can decode.</summary>
    IVideoDecoder Create(string path);
}
