using iTerminal.Core.Audio.Decoders;

namespace iTerminal.Core.Audio;

/// <summary>
/// Picks a decoder for a file. Two tiers: formats decoded in managed code with no external
/// dependency (WAV, MP3), and formats delegated to ffmpeg when it happens to be installed. The
/// split is visible to the UI through <see cref="IsSupported"/> so the playlist can grey out — and
/// the player can explain — files it cannot currently play.
/// </summary>
public static class AudioDecoderFactory
{
    /// <summary>Formats that always work, with no external tooling.</summary>
    public static readonly IReadOnlySet<string> NativeExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".wave", ".mp3", ".mp2" };

    /// <summary>
    /// Formats that need ffmpeg on PATH. Every extension here is one the ffmpeg this build was checked against reads as an
    /// audio file: the container and codec families, the raw and telephony formats, the game-audio streams and the tracker
    /// modules (which need ffmpeg built with libopenmpt). ffmpeg identifies the format from the file's content, so the
    /// extension only decides whether the viewer and the library take the file.
    /// </summary>
    public static readonly IReadOnlySet<string> FfmpegExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Containers and codecs first, as before.
            ".flac", ".ogg", ".oga", ".opus", ".m4a", ".m4b", ".aac", ".wma",
            ".aiff", ".aif", ".alac", ".mp4", ".webm", ".ape", ".mpc", ".wv",
            ".ac3", ".ac4", ".eac3", ".ec3", ".mlp", ".thd", ".truehd", ".dts", ".dtshd",
            ".tta", ".tak", ".mka", ".oma", ".omg", ".aa", ".aa3", ".aax", ".acm", ".aea",
            ".afc", ".apc", ".avr", ".mca", ".dss", ".lc3", ".sbc", ".msbc",
            // Telephony, raw and legacy PCM.
            ".al", ".ul", ".au", ".snd", ".caf", ".sf", ".ircam", ".voc", ".w64",
            ".gsm", ".g722", ".g723_1", ".tco", ".rco", ".g728", ".g729", ".dfpwm",
            ".ivr", ".laf", ".adx", ".aptx", ".aptxhd", ".qoa", ".wa",
            // High-resolution and game-audio streams.
            ".dsf", ".dff", ".genh", ".brstm", ".bfstm", ".bcstm", ".hca", ".binka", ".bonk",
            ".vag", ".xvag", ".wsd", ".xmd", ".vqf", ".vql", ".vqe", ".rka", ".mtaf",
            // Tracker modules.
            ".mod", ".s3m", ".xm", ".it", ".mtm", ".stm", ".669", ".amf", ".ams", ".dbm",
            ".digi", ".dmf", ".dsm", ".dtm", ".far", ".gdm", ".imf", ".j2b", ".m15", ".mdl",
            ".med", ".mmcmp", ".mms", ".mo3", ".mptm", ".mt2", ".nst", ".okt", ".plm", ".psm",
            ".pt36", ".ptm", ".sfx", ".sfx2", ".st26", ".stk", ".stp", ".ult", ".umx", ".wow",
            ".xpk",
        };

    /// <summary>Every extension the player will ever put in a playlist.</summary>
    public static readonly IReadOnlySet<string> AllExtensions =
        new HashSet<string>(NativeExtensions.Concat(FfmpegExtensions), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the extension names an audio format, whether or not ffmpeg is present.</summary>
    public static bool IsAudioFile(string path) =>
        !string.IsNullOrEmpty(path) && AllExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// True when this machine can actually play the file right now: native formats always, ffmpeg
    /// formats only when the binary is on PATH.
    /// </summary>
    public static bool IsSupported(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (NativeExtensions.Contains(extension))
        {
            return true;
        }

        return FfmpegExtensions.Contains(extension) && FfmpegAudioDecoder.IsAvailable();
    }

    /// <summary>
    /// Opens a decoder for <paramref name="path"/>. Throws <see cref="FileNotFoundException"/> when
    /// the file is gone and <see cref="NotSupportedException"/> when nothing on this machine can
    /// decode it — both are surfaced as a status line by the player rather than crashing the pump.
    /// </summary>
    public static IAudioDecoder Create(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Audio file not found.", path);
        }

        var extension = Path.GetExtension(path);

        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wave", StringComparison.OrdinalIgnoreCase))
        {
            // A .wav container can still hold a compressed payload; hand those to ffmpeg.
            if (WaveAudioDecoder.CanDecode(path))
            {
                return new WaveAudioDecoder(path);
            }

            return CreateFfmpeg(path, extension);
        }

        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp2", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new Mp3AudioDecoder(path);
            }
            catch (Exception) when (FfmpegAudioDecoder.IsAvailable())
            {
                return new FfmpegAudioDecoder(path);
            }
        }

        return CreateFfmpeg(path, extension);
    }

    private static IAudioDecoder CreateFfmpeg(string path, string extension)
    {
        if (!FfmpegAudioDecoder.IsAvailable())
        {
            throw new NotSupportedException(
                $"Playing {extension} files needs ffmpeg on PATH. Install ffmpeg, or use WAV/MP3.");
        }

        return new FfmpegAudioDecoder(path);
    }
}
