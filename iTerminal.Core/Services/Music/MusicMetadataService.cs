using iTerminal.Core.Audio;
using iTerminal.Core.Audio.Decoders;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Reads one file's tags and audio format. Tags come from the reader that matches the container
/// (ID3 for MPEG audio, Vorbis comments for FLAC); playing time comes from whichever managed decoder
/// can open the file.
///
/// <para>Like <c>MusicLibraryService.ProbeDuration</c>, the decoders are constructed directly rather
/// than through <see cref="AudioDecoderFactory"/>: viewing a file must never fork ffmpeg, and a
/// format whose length is only knowable that way is honestly reported as unknown instead.</para>
/// </summary>
public sealed class MusicMetadataService : IMusicMetadataService
{
    public bool IsMusicFile(string path) => AudioDecoderFactory.IsAudioFile(path);

    public Task<MusicFileMetadata> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(path), cancellationToken);

    /// <summary>The synchronous body of <see cref="ReadAsync"/>. Public for tests; production reads via the task.</summary>
    public static MusicFileMetadata Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MusicFileMetadata.Empty with { Error = "No file selected." };
        }

        if (!File.Exists(path))
        {
            return MusicFileMetadata.Empty with { Path = path, Error = "The file no longer exists." };
        }

        var format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var metadata = new MusicFileMetadata
        {
            Path = path,
            Format = format,
            CanPlay = AudioDecoderFactory.IsSupported(path),
        };

        try
        {
            metadata = metadata with { Size = new FileInfo(path).Length };
        }
        catch (Exception exception)
        {
            metadata = metadata with { Error = exception.Message };
        }

        if (FlacTagReader.CanRead(path))
        {
            FlacTagReader.FlacInfo flac = FlacTagReader.Read(path);
            return metadata with
            {
                TagSet = flac.Tags,
                Duration = flac.Duration,
                SampleRate = flac.SampleRate,
                Channels = flac.Channels,
                BitsPerSample = flac.BitsPerSample,
            };
        }

        metadata = metadata with { TagSet = MusicTagReader.ReadAll(path) };

        try
        {
            using IAudioDecoder? decoder = CreateManagedDecoder(path);
            if (decoder is not null)
            {
                metadata = metadata with
                {
                    Duration = decoder.Duration,
                    SampleRate = decoder.Format.SampleRate,
                    Channels = decoder.Format.Channels,
                };
            }
        }
        catch
        {
            // A file whose audio cannot be opened still has tags, a name and a size worth showing.
        }

        return metadata;
    }

    /// <summary>The decoder for formats that need no external tooling, or null for everything else.</summary>
    private static IAudioDecoder? CreateManagedDecoder(string path)
    {
        var extension = Path.GetExtension(path);

        if ((extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
             || extension.Equals(".wave", StringComparison.OrdinalIgnoreCase))
            && WaveAudioDecoder.CanDecode(path))
        {
            return new WaveAudioDecoder(path);
        }

        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp2", StringComparison.OrdinalIgnoreCase))
        {
            return new Mp3AudioDecoder(path);
        }

        return null;
    }
}
