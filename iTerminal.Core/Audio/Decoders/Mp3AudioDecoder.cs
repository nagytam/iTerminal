using NLayer;

namespace iTerminal.Core.Audio.Decoders;

/// <summary>
/// MP3 decoding through NLayer, a fully managed MPEG-1/2 Layer I–III decoder. Managed decoding is
/// what lets MP3 — by far the most common format in a music folder — work with no external tools
/// installed on either platform; everything more exotic falls to <see cref="FfmpegAudioDecoder"/>.
///
/// <para>NLayer emits s16le directly via <c>ReadSamplesInt16</c>, so this is a thin adapter with no
/// sample conversion of its own.</para>
/// </summary>
public sealed class Mp3AudioDecoder : IAudioDecoder
{
    private readonly MpegFile _mpegFile;
    private readonly Stream _stream;
    private byte[] _scratch = [];

    public PcmFormat Format { get; }

    public TimeSpan Duration { get; }

    public bool CanSeek => _mpegFile.CanSeek;

    public TimeSpan Position
    {
        get
        {
            try
            {
                return _mpegFile.Time;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }

    public Mp3AudioDecoder(string path)
    {
        // FileShare.Read keeps the file usable elsewhere (viewer, copy) while it plays.
        _stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _mpegFile = new MpegFile(_stream);
            Format = new PcmFormat(_mpegFile.SampleRate, _mpegFile.Channels);
            Duration = _mpegFile.Duration;
        }
        catch
        {
            _stream.Dispose();
            throw;
        }

        if (!Format.IsValid)
        {
            _mpegFile.Dispose();
            _stream.Dispose();
            throw new InvalidDataException("MP3 stream reported an unusable sample rate or channel count.");
        }
    }

    public int Read(Span<byte> buffer)
    {
        if (buffer.Length <= 0)
        {
            return 0;
        }

        if (_scratch.Length < buffer.Length)
        {
            _scratch = new byte[buffer.Length];
        }

        int read;
        try
        {
            read = _mpegFile.ReadSamplesInt16(_scratch, 0, buffer.Length);
        }
        catch
        {
            // A corrupt frame late in the file reads as end-of-stream so the playlist moves on.
            return 0;
        }

        if (read <= 0)
        {
            return 0;
        }

        read -= read % PcmFormat.BytesPerSample;
        _scratch.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    public bool TrySeek(TimeSpan position)
    {
        if (!_mpegFile.CanSeek)
        {
            return false;
        }

        try
        {
            _mpegFile.Time = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _mpegFile.Dispose();
        }
        catch
        {
        }

        _stream.Dispose();
    }
}
